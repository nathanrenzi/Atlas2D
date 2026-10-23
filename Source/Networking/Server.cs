using Riptide;
using Riptide.Transports.Steam;

namespace Atlas2D.Networking.Core
{
    /// <summary>
    /// A host/client server implementation using <see cref="Riptide.Server"/>.
    /// </summary>
    public sealed class Server
    {
        public Riptide.Server RiptideServer { get; private set; }
        public SteamServer SteamServer { get; private set; }
        public NetworkServiceManager Services { get; private set; } = new(true);
        public long TimeStarted { get; private set; } = -1;
        public bool IsRunning => RiptideServer.IsRunning;
        public bool AllowNonHostOwnership { get; set; } = true;
        /// <summary>
        /// A random key the host's own client sends when connecting, so the server knows which client is the host.
        /// </summary>
        internal string HostKey { get; private set; } = "";
        /// <summary>
        /// How many NetworkNodes each client can spawn per second. Spawns over this are rejected.
        /// </summary>
        public int MaxSpawnsPerSecond { get; set; } = 60;

        public event Action<ushort> ClientConnected;
        public event Action<ushort> ClientDisconnected;

        private List<Func<Connection, ValidationResult>> _connectionValidators = new();
        private Queue<(BuiltInMessageType, object, ushort)> _messageQueue = new();
        private Dictionary<uint, List<NetworkNodeData>> _syncingClientSnapshots = new();
        // Sync data from the host for a joining client, collected one node per message until all have arrived
        private Dictionary<ushort, (int Expected, List<NetworkNodeData> Nodes)> _pendingSnapshots = new();
        private Dictionary<ushort, (int Count, long WindowStart)> _spawnCounts = new();
        private Dictionary<string, ushort> _networkGameClassOwners = new();
        private List<string> _networkNodesToRemove = new();
        private List<(string, ushort)> _networkNodesToAdd = new();
        private bool _queueNetworkNodes = false;
        private bool _hostAssigned = false;
        private ushort _hostID = 0;
        private string _serverPassword = null;
        private TransportMode _networkMode;

        public Server(TransportMode networkMode)
        {
            _networkMode = networkMode;
            if(networkMode == TransportMode.SteamP2P)
            {
                SteamServer = new SteamServer();
                RiptideServer = new Riptide.Server(SteamServer);
            }
            else
            {
                RiptideServer = new Riptide.Server();
            }
            RiptideServer.HeartbeatInterval = ProjectSettings.ServerHeartbeatIntervalMilliseconds;
            RiptideServer.HandleConnection = ValidateConnection;
            RiptideServer.ClientConnected += HandleClientConnected;
            RiptideServer.ClientDisconnected += HandleClientDisconnected;
            RiptideServer.MessageReceived += HandleMessageReceived;
        }

        /// <summary>
        /// Should be called as often as possible.
        /// </summary>
        public void ServerUpdate()
        {
            RiptideServer?.Update();
            while (_messageQueue.Count > 0)
            {
                (BuiltInMessageType, object, ushort) message;
                if(_messageQueue.TryDequeue(out message))
                {
                    try
                    {
                        switch (message.Item1)
                        {
                            case BuiltInMessageType.NetworkNodeSpawned:
                                HandleNetworkClassSpawned(message.Item3, (NetworkNodeData)message.Item2);
                                break;
                            case BuiltInMessageType.NetworkNodeUpdated:
                                HandleNetworkClassUpdated(message.Item3, (NetworkNodeUpdatedData)message.Item2);
                                break;
                            case BuiltInMessageType.NetworkNodeDespawned:
                                HandleNetworkClassDespawned(message.Item3, (string)message.Item2);
                                break;
                            case BuiltInMessageType.NetworkNodeSync:
                                HandleNetworkClassSync(message.Item3);
                                break;
                            case BuiltInMessageType.NetworkNodeRequestSyncData:
                                HandleNetworkClassRequestSync(message.Item3, (NetworkGameClassRequestSyncData)message.Item2);
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"(SERVER): Failed to handle {message.Item1} from client [{message.Item3}]. {e}");
                    }
                }
            }
        }

        /// <summary>
        /// Sends a message to a specific client.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        /// <param name="client">The ID of the client this message should be sent to.</param>
        /// <param name="shouldRelease">Should the message be automatically released to the message pool after being sent?
        /// If you intend to use the message after it is sent, set this to false and use <see cref="Message.Release()"/> when done.</param>
        public void Send(Message message, ushort client, bool shouldRelease = true)
        {
            RiptideServer.Send(message, client, shouldRelease);
        }

        /// <summary>
        /// Sends a message to a specific connection.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        /// <param name="toClient">The connection this message should be sent to.</param>
        /// <param name="shouldRelease">Should the message be automatically released to the message pool after being sent?
        /// If you intend to use the message after it is sent, set this to false and use <see cref="Message.Release()"/> when done.</param>
        public void Send(Message message, Connection toClient, bool shouldRelease = true)
        {
            RiptideServer.Send(message, toClient, shouldRelease);
        }

        /// <summary>
        /// Sends a message to all clients.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        /// <param name="shouldRelease">Should the message be automatically released to the message pool after being sent?
        /// If you intend to use the message after it is sent, set this to false and use <see cref="Message.Release()"/> when done.</param>
        public void SendToAll(Message message, bool shouldRelease = true)
        {
            RiptideServer.SendToAll(message, shouldRelease);
        }

        /// <summary>
        /// Sends a message to all clients, except a specific one.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        /// <param name="exceptToClient">The client this message should not be sent to.</param>
        /// <param name="shouldRelease">Should the message be automatically released to the message pool after being sent?
        /// If you intend to use the message after it is sent, set this to false and use <see cref="Message.Release()"/> when done.</param>
        public void SendToAll(Message message, ushort exceptToClient, bool shouldRelease = true)
        {
            RiptideServer.SendToAll(message, exceptToClient, shouldRelease);
        }

        /// <summary>
        /// Starts the server. Note: Port is only used when the server is set to <see cref="TransportMode.NetworkP2P"/> when created.
        /// </summary>
        /// <param name="maxClientCount">The maximum amount of clients that can be connected at a certain time.</param>
        /// <param name="port">The port that the server should use. Note: This is only used when the server is set to <see cref="TransportMode.NetworkP2P"/>.</param>
        /// <param name="password">The (optional) password to use for the server.</param>
        public void Start(ushort maxClientCount, ushort port = 25565, string password = "")
        {
            if (IsRunning) return;
            _serverPassword = password;
            HostKey = Guid.NewGuid().ToString("N");
            if(_networkMode == TransportMode.NetworkP2P)
            {
                RiptideServer.Start(port, maxClientCount, useMessageHandlers: false);
            }
            else
            {
                RiptideServer.Start(ProjectSettings.SteamPort, maxClientCount, useMessageHandlers: false);
            }
            TimeStarted = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;
            RiptideServer.Stop();
            _syncingClientSnapshots.Clear();
            _pendingSnapshots.Clear();
            _spawnCounts.Clear();
            _networkGameClassOwners.Clear();
            _serverPassword = "";
            _hostAssigned = false;
            _hostID = 0;
            HostKey = "";
        }

        #region Handlers
        private void HandleMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            if (e.MessageId < Network.MIN_MESSAGE_TYPE_INTERCEPT || e.MessageId > Network.MAX_MESSAGE_TYPE_INTERCEPT)
            {
                Services.Dispatch(e.MessageId, e.FromConnection.Id, e.Message);
                return;
            }

            BuiltInMessageType messageType = (BuiltInMessageType)e.MessageId;
            Message message = e.Message;
            ushort fromClient = e.FromConnection.Id;
            object data = null;
            try
            {
                switch (messageType)
                {
                    case BuiltInMessageType.NetworkNodeSpawned:
                        data = new NetworkNodeData()
                        {
                            Version = message.GetUInt(),
                            RegisterID = message.GetInt(),
                            NetworkID = message.GetString(),
                            OwnerID = fromClient,
                            Json = message.GetString()
                        };
                        break;
                    case BuiltInMessageType.NetworkNodeUpdated:
                        data = new NetworkNodeUpdatedData()
                        {
                            MessageSendMode = (MessageSendMode)message.GetByte(),
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
                        data = null;
                        break;
                    case BuiltInMessageType.NetworkNodeRequestSyncData:
                        if (fromClient != _hostID)
                        {
                            Debug.LogError($"(SERVER): Non-host client [{fromClient}] tried to send client sync data to the server.");
                            return;
                        }
                        ushort toClient = message.GetUShort();
                        int totalCount = message.GetInt();
                        NetworkNodeData[] classData = Array.Empty<NetworkNodeData>();
                        if (totalCount > 0)
                        {
                            classData = new NetworkNodeData[1];
                            classData[0].Version = message.GetUInt();
                            classData[0].RegisterID = message.GetInt();
                            classData[0].NetworkID = message.GetString();
                            classData[0].OwnerID = message.GetUShort();
                            classData[0].Json = message.GetString();
                        }
                        data = new NetworkGameClassRequestSyncData()
                        {
                            ToClient = toClient,
                            TotalCount = totalCount,
                            GameClasses = classData
                        };
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"(SERVER): Could not read {messageType} from client [{fromClient}]. {exception}");
                return;
            }
            _messageQueue.Enqueue((messageType, data, fromClient));
        }

        /// <summary>
        /// Returns why a spawn from a client is invalid, or null if it's fine.
        /// </summary>
        private string ValidateSpawn(ushort client, NetworkNodeData data)
        {
            if (data.RegisterID < 0 || data.RegisterID >= Network.NetworkNodeRegister.Count)
                return $"unknown register ID {data.RegisterID}";
            if (string.IsNullOrEmpty(data.NetworkID) || data.NetworkID.Length > Network.MAX_NETWORK_ID_LENGTH)
                return "invalid NetworkID";
            if (data.Json == null || System.Text.Encoding.UTF8.GetByteCount(data.Json) > Network.MAX_NODE_JSON_BYTES)
                return $"JSON larger than {Network.MAX_NODE_JSON_BYTES} bytes";

            // Count spawns per client in one second windows
            long now = Environment.TickCount64;
            if (!_spawnCounts.TryGetValue(client, out var spawns) || now - spawns.WindowStart >= 1000)
            {
                spawns = (0, now);
            }
            spawns.Count++;
            _spawnCounts[client] = spawns;
            if (spawns.Count > MaxSpawnsPerSecond)
                return $"more than {MaxSpawnsPerSecond} spawns per second";

            return null;
        }

        private void HandleNetworkClassRequestSync(ushort client, NetworkGameClassRequestSyncData data)
        {
            if(client != _hostID)
            {
                Debug.LogError($"(SERVER): Non-host client [{client}] tried to send client sync data to the server.");
                return;
            }

            if (data.TotalCount > 0)
            {
                if (!_pendingSnapshots.TryGetValue(data.ToClient, out var pending) || pending.Expected != data.TotalCount)
                {
                    pending = (data.TotalCount, new List<NetworkNodeData>());
                }
                pending.Nodes.AddRange(data.GameClasses);
                _pendingSnapshots[data.ToClient] = pending;

                // Wait until every node has arrived
                if (pending.Nodes.Count < pending.Expected) return;
                _pendingSnapshots.Remove(data.ToClient);
                _syncingClientSnapshots[data.ToClient] = pending.Nodes;
            }

            Debug.Log("(SERVER): Received requested sync data from host. Asking client to sync...");
            Message returnMessage = Message.Create(MessageSendMode.Reliable, (ushort)BuiltInMessageType.NetworkNodeSync);
            returnMessage.AddInt(Math.Max(data.TotalCount, 0));
            Send(returnMessage, data.ToClient);
        }
        private void HandleNetworkClassSpawned(ushort client, NetworkNodeData data)
        {
            string invalidReason = ValidateSpawn(client, data);
            if (invalidReason != null)
            {
                Debug.LogWarning($"(SERVER): Rejected spawn from client [{client}]: {invalidReason}.");
                // Only reply if the ID fits in a message
                if (!string.IsNullOrEmpty(data.NetworkID) && data.NetworkID.Length <= Network.MAX_NETWORK_ID_LENGTH)
                {
                    Message rejectMessage = Message.Create(MessageSendMode.Reliable, (ushort)BuiltInMessageType.NetworkNodeDespawned);
                    rejectMessage.AddString(data.NetworkID);
                    Send(rejectMessage, client);
                }
                return;
            }

            bool despawn = false;
            if (!AllowNonHostOwnership && client != _hostID)
            {
                Debug.LogWarning($"(SERVER): A non-host client [{client}] tried to spawn a network game class. Using the " +
                    $"current networking settings, this is not allowed.");
                despawn = true;
            }

            if (!despawn && client != _hostID && Network.HostOnlyRegisterIDs.Contains(data.RegisterID))
            {
                Debug.LogWarning($"(SERVER): Non-host client [{client}] tried to spawn a host-only NetworkNode " +
                    $"(register ID {data.RegisterID}). Rejecting.");
                despawn = true;
            }

            if (_networkGameClassOwners.ContainsKey(data.NetworkID) || despawn)
            {
                if (!despawn) Debug.LogError($"(SERVER): Network game class with id [{data.NetworkID}] already exists on the server. Cannot spawn.");
                Message despawnMessage = Message.Create(MessageSendMode.Reliable, (ushort)BuiltInMessageType.NetworkNodeDespawned);
                despawnMessage.AddString(data.NetworkID);
                Send(despawnMessage, client);
                return;
            }

            if(_queueNetworkNodes)
            {
                _networkNodesToAdd.Add((data.NetworkID, client));
            }
            else
            {
                _networkGameClassOwners.Add(data.NetworkID, client);
            }

            Message returnMessage = Message.Create(MessageSendMode.Reliable,
                (ushort)BuiltInMessageType.NetworkNodeSpawned);
            returnMessage.AddUInt(data.Version);
            returnMessage.AddInt(data.RegisterID);
            returnMessage.AddString(data.NetworkID);
            returnMessage.AddUShort(client);
            returnMessage.AddString(data.Json);
            SendToAll(returnMessage);
        }
        private void HandleNetworkClassUpdated(ushort client, NetworkNodeUpdatedData data)
        {
            if (data.NetworkID == null || data.Json == null || !Enum.IsDefined(data.MessageSendMode)
                || System.Text.Encoding.UTF8.GetByteCount(data.Json) > Network.MAX_NODE_JSON_BYTES)
            {
                Debug.LogWarning($"(SERVER): Rejected an invalid update from client [{client}].");
                return;
            }

            if(!_networkGameClassOwners.ContainsKey(data.NetworkID))
            {
                Debug.LogError($"(SERVER): Client {client} is trying to update a network game class with " +
                    $"id [{data.NetworkID}] that doesn't exist!");
                return;
            }

            // Checking if client is owner of object
            if (_networkGameClassOwners[data.NetworkID] != client)
            {
                Debug.LogWarning($"(SERVER): Client {client} is trying to update a network game class with " +
                    $"id [{data.NetworkID}] that doesn't belong to them!");
                return;
            }

            Message returnMessage = Message.Create(data.MessageSendMode, (ushort)BuiltInMessageType.NetworkNodeUpdated);
            returnMessage.AddString(data.NetworkID);
            returnMessage.AddUInt(data.Version);
            returnMessage.AddUShort(data.Type);
            returnMessage.AddString(data.Json);
            SendToAll(returnMessage, client);
        }
        private void HandleNetworkClassSync(ushort client)
        {
            if (!_syncingClientSnapshots.ContainsKey(client))
            {
                Debug.LogError($"(SERVER): Client [{client}] tried to get sync data from server without permission.");
                return;
            }

            Debug.Log($"(SERVER): Received sync confirmation from client {client}. Sending data...");
            List<NetworkNodeData> dataList = _syncingClientSnapshots[client];
            foreach (var data in dataList)
            {
                Message returnMessage = Message.Create(MessageSendMode.Reliable, (ushort)BuiltInMessageType.NetworkNodeSync);
                returnMessage.AddUInt(data.Version);
                returnMessage.AddInt(data.RegisterID);
                returnMessage.AddString(data.NetworkID);
                returnMessage.AddUShort(data.OwnerID);
                returnMessage.AddString(data.Json);
                Send(returnMessage, client);
            }
            _syncingClientSnapshots.Remove(client);
        }
        private void HandleNetworkClassDespawned(ushort client, string networkID, bool exceptClient = false)
        {
            if (!_networkGameClassOwners.ContainsKey(networkID))
            {
                Debug.LogWarning($"(SERVER): Client {client} tried to despawn network game class with id [{networkID}] that doesn't exist!");
                return;
            }

            if (_networkGameClassOwners[networkID] != client)
            {
                Debug.LogWarning($"(SERVER): Client {client} tried to despawn network game class with id [{networkID}] which doesn't belong " +
                    "to them!");
                return;
            }
            
            if(_queueNetworkNodes)
            {
                _networkNodesToRemove.Add(networkID);
            }
            else
            {
                _networkGameClassOwners.Remove(networkID);
            }
            Message returnMessage = Message.Create(MessageSendMode.Reliable, (ushort)BuiltInMessageType.NetworkNodeDespawned);
            returnMessage.AddString(networkID);
            if(exceptClient)
            {
                // If this client just disconnected, excluding them from message
                SendToAll(returnMessage, client);
            }
            else
            {
                SendToAll(returnMessage);
            }
        }
        private void HandleClientConnected(object? sender, ServerConnectedEventArgs e)
        {
            if(e.Client.Id == _hostID)
            {
                Message hostInitializeMessage = Message.Create(MessageSendMode.Reliable,
                    (ushort)BuiltInMessageType.NetworkNodeSync);
                hostInitializeMessage.AddInt(0);
                Send(hostInitializeMessage, _hostID);
                ClientConnected?.Invoke(e.Client.Id);
                return;
            }

            Debug.Log("(SERVER): Client joined, sending sync signal.");
            Message toHostMessage = Message.Create(MessageSendMode.Reliable,
                (ushort)BuiltInMessageType.NetworkNodeRequestSyncData);
            toHostMessage.AddUShort(e.Client.Id);
            Send(toHostMessage, _hostID);
            ClientConnected?.Invoke(e.Client.Id);
        }
        private void HandleClientDisconnected(object? sender, ServerDisconnectedEventArgs e)
        {
            if(e.Client.Id == _hostID)
            {
                Debug.Log("(SERVER): Until host transferring is implemented, the server will stop when the host leaves.");
                Stop();
                return;
            }

            _syncingClientSnapshots.Remove(e.Client.Id);
            _pendingSnapshots.Remove(e.Client.Id);
            _spawnCounts.Remove(e.Client.Id);

            _queueNetworkNodes = true;
            foreach (var pair in _networkGameClassOwners)
            {
                if(pair.Value == e.Client.Id)
                {
                    HandleNetworkClassDespawned(e.Client.Id, pair.Key, true);
                }
            }
            _queueNetworkNodes = false;
            PopQueues();
            ClientDisconnected?.Invoke(e.Client.Id);
        }
        #endregion

        private void PopQueues()
        {
            if (_queueNetworkNodes) return;
            for (int i = 0; i < _networkNodesToAdd.Count; i++)
            {
                _networkGameClassOwners.Add(_networkNodesToAdd[i].Item1, _networkNodesToAdd[i].Item2);
            }
            _networkNodesToAdd.Clear();
            for (int i = 0; i < _networkNodesToRemove.Count; i++)
            {
                _networkGameClassOwners.Remove(_networkNodesToRemove[i]);
            }
            _networkNodesToRemove.Clear();
        }

        /// <summary>
        /// Adds a connection validator to the server. Connecting clients must pass all validators before
        /// they are allowed to connect.
        /// </summary>
        public void AddConnectionValidator(Func<Connection, ValidationResult> validator)
        {
            if(!_connectionValidators.Contains(validator))
            {
                _connectionValidators.Add(validator);
            }
        }

        /// <summary>
        /// Removes a connection validator from the server.
        /// </summary>
        /// <param name="validator"></param>
        /// <returns>true if validator is successfully removed.</returns>
        public bool RemoveConnectionValidator(Func<Connection, ValidationResult> validator)
        {
            return _connectionValidators.Remove(validator);
        }

        private void ValidateConnection(Connection pendingConnection, Message connectMessage)
        {
            string password = connectMessage.GetString();
            string hostKey = connectMessage.UnreadBits > 0 ? connectMessage.GetString() : "";

            // The host's own client skips the password and validators
            if (!_hostAssigned && HostKey != "" && hostKey == HostKey)
            {
                RiptideServer.Accept(pendingConnection);
                if (pendingConnection.Id != 0)
                {
                    _hostID = pendingConnection.Id;
                    _hostAssigned = true;
                }
                return;
            }

            // Nobody else can join until the host has
            if (!_hostAssigned)
            {
                RiptideServer.Reject(pendingConnection, Message.Create().AddString("The host hasn't joined yet."));
                return;
            }

            if (_serverPassword != "" && !_serverPassword.Equals(password))
            {
                RiptideServer.Reject(pendingConnection, Message.Create().AddString("Incorrect password."));
                return;
            }

            for (int i = 0; i < _connectionValidators.Count; i++)
            {
                ValidationResult result = _connectionValidators[i].Invoke(pendingConnection);
                if (!result.Success)
                {
                    RiptideServer.Reject(pendingConnection, Message.Create().AddString(result.FailureReason));
                    return;
                }
            }

            RiptideServer.Accept(pendingConnection);
        }
    }
}
