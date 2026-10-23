using Riptide;
using Riptide.Transports.Steam;

namespace Atlas2D.Networking.Core
{
    /// <summary>
    /// A host/client client implementation using <see cref="Riptide.Client"/>.
    /// </summary>
    public sealed class Client : IDisposable
    {
        public Riptide.Client RiptideClient { get; private set; }
        public SteamClient SteamClient { get; private set; }
        public NetworkServiceManager Services { get; private set; } = new(false);
        public Dictionary<string, NetworkNode> NetworkNodes { get; private set; } = new();
        public ushort ID => RiptideClient.Id;
        public bool IsConnected => RiptideClient.IsConnected;
        public bool IsConnecting => RiptideClient.IsConnecting;

        public event Action NetworkNodesLoaded;
        public event Action<RejectReason, string?> ConnectionFailed;
        public event Action ConnectionSuccessful;
        public event Action<DisconnectReason> Disconnected;
        public event Action<ushort> ClientConnected;
        public event Action<ushort> ClientDisconnected;
        public event Action<string> NetworkNodeSpawned;

        private Queue<(NetworkNode, string, ushort)> _syncingNetworkNodes = new();
        private Server _server;
        private Queue<(BuiltInMessageType, object)> _messageQueue = new();
        private bool _isSyncing = false;
        private bool _isPaused = false;
        private int _syncCount = 0;
        private TransportMode _networkMode;

        public Client(TransportMode networkMode)
        {
            _networkMode = networkMode;
            if (networkMode == TransportMode.SteamP2P)
            {
                SteamClient = new SteamClient();
                RiptideClient = new Riptide.Client(SteamClient);
            }
            else
            {
                RiptideClient = new Riptide.Client();
            }
            RiptideClient.ConnectionFailed += HandleConnectionFailed;
            RiptideClient.Connected += HandleConnected;
            RiptideClient.Disconnected += HandleDisconnected;
            RiptideClient.MessageReceived += HandleMessageReceived;
            RiptideClient.ClientConnected += (obj, e) => ClientConnected?.Invoke(e.Id);
            RiptideClient.ClientDisconnected += (obj, e) => ClientDisconnected?.Invoke(e.Id);
        }

        public void SetServer(Server server)
        {
            if (_server != null) return;
            _server = server;
            SteamClient?.ChangeLocalServer(server.SteamServer);
        }

        public void ClientUpdate()
        {
            RiptideClient?.Update();
            if (!_isPaused)
            {
                while(_messageQueue.Count > 0)
                {
                    (BuiltInMessageType, object) data;
                    if (!_messageQueue.TryDequeue(out data)) break;
                    if(_isSyncing && data.Item1 != BuiltInMessageType.NetworkNodeSync)
                    {
                        _messageQueue.Enqueue(data);
                        break;
                    }

                    switch (data.Item1)
                    {
                        case BuiltInMessageType.NetworkNodeSpawned:
                            HandleNetworkNodeSpawned((NetworkNodeData)data.Item2);
                            break;
                        case BuiltInMessageType.NetworkNodeUpdated:
                            HandleNetworkNodeUpdated((NetworkNodeUpdatedData)data.Item2);
                            break;
                        case BuiltInMessageType.NetworkNodeDespawned:
                            HandleNetworkNodeDespawned((string)data.Item2);
                            break;
                        case BuiltInMessageType.NetworkNodeSync:
                            if(_isSyncing)
                                HandleNetworkNodeSyncSpawn((NetworkNodeSyncSpawnData)data.Item2);
                            else
                                HandleNetworkNodeSyncStart((int)data.Item2);
                            break;
                        case BuiltInMessageType.NetworkNodeRequestSyncData:
                            HandleNetworkNodeRequestSyncData((ushort)data.Item2);
                            break;
                    }
                }
            }
        }

        public void Send(Message message, bool shouldRelease = true)
        {
            RiptideClient.Send(message, shouldRelease);
        }

        public bool Connect(string address, ushort port = 25565, string password = "")
        {
            if (IsConnected || IsConnecting)
            {
                Debug.LogError("Client cannot connect, already connected to a server.");
                return false;
            }

            Message message = Message.Create();
            message.AddString(password);
            if(_networkMode == TransportMode.NetworkP2P)
            {
                return RiptideClient.Connect($"{(address == "localhost" ? "127.0.0.1" : address)}:{port}", message: message, useMessageHandlers: false);
            }
            else
            {
                SteamClient.SetLastUsedPassword(password);
                if(address == "localhost" || address == "127.0.0.1")
                    return RiptideClient.Connect($"{address}", message: message, useMessageHandlers: false);
                else
                    return RiptideClient.Connect($"{address}:{ProjectSettings.SteamPort}", message: message, useMessageHandlers: false);
            }
        }

        public void Disconnect()
        {
            RiptideClient.Disconnect();
            _messageQueue.Clear();
            if(_networkMode == TransportMode.SteamP2P)
                SteamClient.SetLastUsedPassword("");
        }

        /// <summary>
        /// Retrieves the <see cref="NetworkNode"/> with the given NetworkID.
        /// </summary>
        public NetworkNode GetNetworkNode(string networkID)
        {
            return NetworkNodes.TryGetValue(networkID, out NetworkNode node) ? node : null;
        }

        /// <summary>
        /// Attempts to retrieve the <see cref="NetworkNode"/> with the given NetworkID.
        /// </summary>
        public bool TryGetNetworkNode(string networkID, out NetworkNode networkNode)
        {
            return NetworkNodes.TryGetValue(networkID, out networkNode);
        }

        #region Steam Methods
        public void SteamOpenInviteMenu()
        {
            if(_networkMode != TransportMode.SteamP2P)
            {
                Debug.LogError("Cannot open steam invite menu when steam networking is not currently being used!");
                return;
            }
            SteamClient.OpenInviteMenu();
        }

        public void SteamInviteFriend(Steamworks.CSteamID steamIDFriend)
        {
            if (_networkMode != TransportMode.SteamP2P)
            {
                Debug.LogError("Cannot invite steam friend when steam networking is not currently being used!");
                return;
            }
            SteamClient.InviteFriend(steamIDFriend);
        }
        #endregion

        #region Handlers
        private void HandleMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            if (e.MessageId < Network.MIN_MESSAGE_TYPE_INTERCEPT || e.MessageId > Network.MAX_MESSAGE_TYPE_INTERCEPT)
            {
                Services.Dispatch(e.MessageId, e.Message);
                return;
            }

            BuiltInMessageType messageType = (BuiltInMessageType)e.MessageId;
            Message message = e.Message;
            object data = null;
            switch (messageType)
            {
                case BuiltInMessageType.NetworkNodeSpawned:
                    data = new NetworkNodeData()
                    {
                        Version = message.GetUInt(),
                        RegisterID = message.GetInt(),
                        NetworkID = message.GetString(),
                        OwnerID = message.GetUShort(),
                        Json = message.GetString()
                    };
                    break;
                case BuiltInMessageType.NetworkNodeUpdated:
                    data = new NetworkNodeUpdatedData()
                    {
                        NetworkID = message.GetString(),
                        Version = message.GetUInt(),
                        Type = message.GetUShort(),
                        Json = message.GetString()
                    };
                    break;
                case BuiltInMessageType.NetworkNodeDespawned:
                    data = message.GetString();
                    break;
                case BuiltInMessageType.NetworkNodeSync:
                    if (_isSyncing)
                    {
                        data = new NetworkNodeSyncSpawnData()
                        {
                            Version = message.GetUInt(),
                            RegisterID = message.GetInt(),
                            NetworkID = message.GetString(),
                            ClientID = message.GetUShort(),
                            Json = message.GetString()
                        };
                    }
                    else
                    {
                        data = message.GetInt();
                    }
                    break;
                case BuiltInMessageType.NetworkNodeRequestSyncData:
                    data = message.GetUShort();
                    break;
            }
            _messageQueue.Enqueue((messageType, data));
        }

        private void HandleNetworkNodeRequestSyncData(ushort client)
        {
            _isPaused = true;
            if (client == ID) return;
            Message returnMessage = Message.Create(MessageSendMode.Reliable,
                (ushort)BuiltInMessageType.NetworkNodeRequestSyncData);
            returnMessage.AddUShort(client);
            int initializedCount = 0;
            foreach (var node in NetworkNodes.Values)
            {
                if (node.IsNetworkInitialized) initializedCount++;
            }
            returnMessage.AddInt(initializedCount);
            foreach (var node in NetworkNodes.Values)
            {
                if (!node.IsNetworkInitialized) continue;
                returnMessage.AddUInt(node.UpdateVersion);
                returnMessage.AddInt(node.GetRegisterID());
                returnMessage.AddString(node.NetworkID);
                returnMessage.AddUShort(node.OwnerID);
                returnMessage.AddString(node.ToJson());
            }
            Debug.Log($"(CLIENT): Sending requested sync data to server, total count {initializedCount}.");
            Send(returnMessage);
            _isPaused = false;
        }

        private void HandleNetworkNodeSpawned(NetworkNodeData data)
        {
            if (data.OwnerID == ID)
            {
                if (!NetworkNodes.ContainsKey(data.NetworkID))
                {
                    Debug.LogError($"(CLIENT): Could not initialize locally spawned NetworkNode with id [{data.NetworkID}].");
                    return;
                }
                NetworkNodes[data.NetworkID].SetUpdateVersion(data.Version);
                NetworkNodes[data.NetworkID].NetworkInitialize(data.NetworkID, data.OwnerID, this, _server);
            }
            else
            {
                NetworkNode node = Network.NetworkNodeRegister[data.RegisterID](data.Json);
                node.SetUpdateVersion(data.Version);
                NetworkNodes.Add(data.NetworkID, node);
                node.NetworkInitialize(data.NetworkID, data.OwnerID, this, _server);
                node.Enable();
            }
            NetworkNodeSpawned?.Invoke(data.NetworkID);
        }

        private void HandleNetworkNodeUpdated(NetworkNodeUpdatedData data)
        {
            NetworkNode node = GetNetworkNode(data.NetworkID);
            if (node != null && node.CheckAndHandleUpdateVersion(data.Type, data.Version))
            {
                node.ReceiveData(data.Type, data.Json);
            }
        }

        private void HandleNetworkNodeDespawned(string networkID)
        {
            NetworkNode node = GetNetworkNode(networkID);
            if (node != null)
            {
                NetworkNodes.Remove(networkID);
                node.Despawn(false);
            }
        }

        private void HandleNetworkNodeSyncSpawn(NetworkNodeSyncSpawnData data)
        {
            if (data.ClientID == ID)
            {
                if (!NetworkNodes.ContainsKey(data.NetworkID))
                {
                    Debug.LogError($"(CLIENT): Could not initialize locally spawned NetworkNode with id [{data.NetworkID}].");
                    return;
                }
                NetworkNodes[data.NetworkID].SetUpdateVersion(data.Version);
                _syncingNetworkNodes.Enqueue((NetworkNodes[data.NetworkID], data.NetworkID, data.ClientID));
            }
            else
            {
                NetworkNode node = Network.NetworkNodeRegister[data.RegisterID](data.Json);
                node.SetUpdateVersion(data.Version);
                NetworkNodes.Add(data.NetworkID, node);
                _syncingNetworkNodes.Enqueue((node, data.NetworkID, data.ClientID));
            }
            _syncCount--;
            Debug.Log($"(CLIENT): Received sync data from server, {_syncCount} left.");
            if (_syncCount == 0)
            {
                _isSyncing = false;
                while (_syncingNetworkNodes.Count != 0)
                {
                    (NetworkNode, string, ushort) nodeData = _syncingNetworkNodes.Dequeue();
                    nodeData.Item1.NetworkInitialize(nodeData.Item2, nodeData.Item3, this, _server);
                    NetworkNodeSpawned?.Invoke(nodeData.Item2);
                }
                NetworkNodesLoaded?.Invoke();
                Debug.Log("(CLIENT): Done syncing!");
            }
        }

        private void HandleNetworkNodeSyncStart(int syncCount)
        {
            _syncCount = syncCount;
            if (syncCount == 0)
            {
                NetworkNodesLoaded?.Invoke();
                Debug.Log("(CLIENT): Received sync signal from server with no data to sync, done syncing!");
                return;
            }
            _isSyncing = true;
            Debug.Log($"(CLIENT): Received sync signal from server, total count: {syncCount}. Asking server for data...");
            Send(Message.Create(MessageSendMode.Reliable, (ushort)BuiltInMessageType.NetworkNodeSync));
        }

        private void HandleConnectionFailed(object? sender, ConnectionFailedEventArgs e)
        {
            string? message = null;
            try
            {
                if (e.Message != null && e.Message.UnreadBits > 0)
                    message = e.Message.GetString();
            } catch { }
            Debug.Log($"(CLIENT): Connection failed. Reason: {(e.Reason == RejectReason.Custom ? message : e.Reason)}");
            ConnectionFailed?.Invoke(e.Reason, message);
        }

        private void HandleConnected(object? sender, EventArgs e)
        {
            ConnectionSuccessful?.Invoke();
        }

        private void HandleDisconnected(object? sender, DisconnectedEventArgs e)
        {
            bool notifyServer = e.Reason != DisconnectReason.ServerStopped
                             && e.Reason != DisconnectReason.Kicked
                             && e.Reason != DisconnectReason.TimedOut;

            foreach (var pair in NetworkNodes)
            {
                pair.Value.Despawn(!notifyServer ? false : !pair.Value.IsOwner ? false : true);
            }
            NetworkNodes.Clear();
            _syncingNetworkNodes.Clear();
            _messageQueue.Clear();
            _isPaused = false;
            _isSyncing = false;
            _syncCount = 0;
            Disconnected?.Invoke(e.Reason);
        }
        #endregion

        public void Dispose()
        {
            Disconnect();
        }
    }
}
