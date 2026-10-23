using Riptide;

namespace Atlas2D.Networking
{
    /// <summary>
    /// An abstract node that provides the boilerplate for networked game objects.
    /// Extends <see cref="TransformNode"/> so any subclass has a position in the world.
    /// Subclasses must implement <see cref="INetworkFactory"/> to be automatically discovered
    /// by <see cref="Network.RegisterAll"/>.
    /// </summary>
    public abstract class NetworkNode : TransformNode
    {
        private struct DependencyCallback
        {
            public Type ExpectedType;
            public Action<NetworkNode> Callback;
        }

        private static readonly Dictionary<Type, int> _registerIDs = new();

        public string NetworkID { get; private set; } = string.Empty;
        public ushort OwnerID { get; private set; } = ushort.MaxValue;
        public bool IsOwner { get; private set; } = false;
        public bool IsNetworkInitialized { get; private set; }
        public uint UpdateVersion { get; private set; } = 0;
        public bool RemoveLocallyOnDespawn { get; set; }

        /// <summary>
        /// When true, only the host may spawn this node. Annotate the subclass with
        /// <see cref="HostOnlyAttribute"/> to restrict spawning, or override this property
        /// to enforce the restriction dynamically.
        /// </summary>
        public virtual bool HostOnly => GetType().IsDefined(typeof(HostOnlyAttribute), inherit: true);

        public event Action OnNetworkInitializedEvent;
        public event Action OnNetworkDespawnedEvent;

        protected Core.Client _client;
        protected Core.Server _server;

        private Dictionary<string, DependencyCallback> _dependencies = new();
        private int _totalDependencies = 0;
        private int _markDispose = 0;

        protected NetworkNode() { }

        /// <summary>
        /// Cleans up network state when the node is disposed. If this client owns the node, it's despawned
        /// for everyone. Subclasses should call base.OnDispose() to ensure network teardown runs.
        /// </summary>
        protected override void OnDispose()
        {
            if (_markDispose == 1) Engine.Game.LateUpdateEvent -= DisposeIfMarked;

            if (IsNetworkInitialized)
            {
                Core.Client client = _client;
                string networkID = NetworkID;
                bool isOwner = IsOwner;
                Despawn(sendMessageToServer: true);
                // Owned nodes are removed when the server confirms the despawn
                if (!isOwner) client?.NetworkNodes.Remove(networkID);
            }
            else if (IsOwner && _client != null && !string.IsNullOrEmpty(NetworkID))
            {
                // The spawn was sent but not confirmed yet
                Message message = Message.Create(MessageSendMode.Reliable, (ushort)BuiltInMessageType.NetworkNodeDespawned);
                message.AddString(NetworkID);
                _client.Send(message);
                _client.NetworkNodes.Remove(NetworkID);
                Reset();
            }
        }

        /// <summary>
        /// Marks the node for disposal on the next late update. Safe to call from any thread.
        /// </summary>
        public void MarkDispose()
        {
            // Only subscribes when needed, so idle nodes don't each add a LateUpdate handler
            if (Interlocked.Exchange(ref _markDispose, 1) == 0)
            {
                Engine.Game.LateUpdateEvent += DisposeIfMarked;
            }
        }

        private void DisposeIfMarked()
        {
            Engine.Game.LateUpdateEvent -= DisposeIfMarked;
            _markDispose = 2;
            Dispose();
        }

        internal static void AssignRegisterID(Type type, int id)
        {
            _registerIDs[type] = id;
        }

        /// <summary>
        /// Returns the register ID assigned to this class during <see cref="Network.RegisterAll"/>.
        /// </summary>
        protected internal int GetRegisterID() => _registerIDs[GetType()];

        /// <summary>
        /// Sends a request to the server to spawn this object.
        /// </summary>
        public void Spawn(string networkID = null, bool removeLocallyOnDespawn = false,
            Core.Client customClient = null, Core.Server customServer = null)
        {
            if (IsNetworkInitialized) return;

            _client = customClient ?? Network.Instance.Client;
            _server = customServer ?? Network.Instance.Server;

            if (string.IsNullOrEmpty(networkID))
                networkID = Guid.NewGuid().ToString("N");

            if (networkID.Length > Network.MAX_NETWORK_ID_LENGTH)
            {
                Debug.LogError($"NetworkNode ID [{networkID}] is longer than {Network.MAX_NETWORK_ID_LENGTH} characters. Cannot spawn.");
                return;
            }

            RemoveLocallyOnDespawn = removeLocallyOnDespawn;

            if (HostOnly && !Network.Instance.Server.IsRunning)
            {
                Debug.LogError($"[Network] {GetType().Name} is host-only and cannot be spawned by non-host clients.");
                return;
            }

            if (!_client.IsConnected)
            {
                Debug.LogError($"Trying to spawn NetworkNode with id [{networkID}] before client is connected!");
                return;
            }
            if (_client.NetworkNodes.ContainsKey(networkID))
            {
                Debug.LogError($"NetworkNode with id [{networkID}] already exists on the client. Cannot spawn.");
                return;
            }

            string json = ToJson();
            if (System.Text.Encoding.UTF8.GetByteCount(json) > Network.MAX_NODE_JSON_BYTES)
            {
                Debug.LogError($"{GetType().Name} spawn JSON is larger than {Network.MAX_NODE_JSON_BYTES} bytes. Cannot spawn.");
                return;
            }

            OwnerID = _client.ID;
            IsOwner = true;
            NetworkID = networkID;
            _client.NetworkNodes.Add(NetworkID, this);

            Message message = Message.Create(MessageSendMode.Reliable,
                (ushort)BuiltInMessageType.NetworkNodeSpawned);
            message.AddUInt(UpdateVersion);
            message.AddInt(GetRegisterID());
            message.AddString(networkID);
            message.AddString(json);
            _client.Send(message);
        }

        /// <summary>
        /// Sends a request to the server to despawn this object (if owned by local player).
        /// </summary>
        public void Despawn(bool sendMessageToServer = true)
        {
            if (!IsNetworkInitialized) return;
            IsNetworkInitialized = false;

            if (IsOwner && sendMessageToServer)
            {
                Message message = Message.Create(MessageSendMode.Reliable,
                    (ushort)BuiltInMessageType.NetworkNodeDespawned);
                message.AddString(NetworkID);
                _client.Send(message);
                OnNetworkDespawnedEvent?.Invoke();
                OnDespawned();
                Reset();
                if (RemoveLocallyOnDespawn)
                    Dispose();
            }
            else if (RemoveLocallyOnDespawn)
            {
                OnNetworkDespawnedEvent?.Invoke();
                OnDespawned();
                Reset();
                Dispose();
            }
            else
            {
                OnNetworkDespawnedEvent?.Invoke();
                OnDespawned();
                Reset();
            }
        }

        /// <summary>
        /// Sends data to the server registered under this object's NetworkID.
        /// </summary>
        /// <param name="type">The type of data being sent. Leave default if only one update type is ever sent.</param>
        protected void Send(MessageSendMode sendMode, string json, ushort type = 0)
        {
            if (!IsOwner)
            {
                Debug.LogError("Cannot send data from a NetworkNode that does not belong to this client.");
                return;
            }
            if (!IsNetworkInitialized)
            {
                Debug.LogWarning("Trying to send a message from a NetworkNode that has not been initialized yet.");
                return;
            }
            if (System.Text.Encoding.UTF8.GetByteCount(json) > Network.MAX_NODE_JSON_BYTES)
            {
                Debug.LogError($"{GetType().Name} update JSON is larger than {Network.MAX_NODE_JSON_BYTES} bytes. Cannot send.");
                return;
            }
            UpdateVersion++;
            Message message = Message.Create(sendMode, (ushort)BuiltInMessageType.NetworkNodeUpdated);
            message.AddByte((byte)sendMode);
            message.AddString(NetworkID);
            message.AddUInt(UpdateVersion);
            message.AddUShort(type);
            message.AddString(json);
            _client.Send(message);
        }

        /// <summary>
        /// Called by the client when the server confirms a spawn.
        /// </summary>
        internal void NetworkInitialize(string networkID, ushort ownerID, Core.Client client, Core.Server server)
        {
            if (IsNetworkInitialized) return;

            _client = client;
            _server = server;
            NetworkID = networkID;
            OwnerID = ownerID;
            IsOwner = client.ID == ownerID;
            IsNetworkInitialized = true;
            Network.Instance.Client.NetworkNodeSpawned += CheckDependencyAndInvokeCallback;
            OnNetworkInitializedEvent?.Invoke();
            OnNetworkInitialized();
        }

        internal void SetUpdateVersion(uint newVersion)
        {
            if (newVersion > UpdateVersion)
                UpdateVersion = newVersion;
        }

        /// <summary>
        /// Adds a <see cref="NetworkNode"/> dependency and invokes a callback once it is initialized,
        /// or immediately if it already is. Once all added dependencies have resolved,
        /// <see cref="OnDependenciesInitialized"/> is called.
        /// </summary>
        protected void AddDependency<T>(string networkID, Action<T> onNetworkIDInitialized) where T : NetworkNode
        {
            _totalDependencies++;
            _dependencies.Add(networkID, new DependencyCallback
            {
                ExpectedType = typeof(T),
                Callback = obj => onNetworkIDInitialized((T)obj)
            });
            if (Network.Instance.Client.NetworkNodes.ContainsKey(networkID))
            {
                CheckDependencyAndInvokeCallback(networkID);
            }
        }

        private void CheckDependencyAndInvokeCallback(string networkID)
        {
            if (_dependencies.ContainsKey(networkID))
            {
                NetworkNode networkNode = Network.Instance.Client.GetNetworkNode(networkID);
                DependencyCallback callback = _dependencies[networkID];
                if (callback.ExpectedType.IsInstanceOfType(networkNode))
                {
                    callback.Callback(networkNode);
                }
                else
                {
                    Debug.LogError($"Dependency [{networkID}] expected type {callback.ExpectedType}, " +
                        $"got {networkNode.GetType()} in NetworkNode [{NetworkID}]");
                }
                _dependencies.Remove(networkID);
                if (_dependencies.Count == 0 && _totalDependencies > 0)
                    OnDependenciesInitialized();
            }
        }

        private void Reset()
        {
            _client = null;
            IsOwner = false;
            OwnerID = 0;
            NetworkID = "";
            UpdateVersion = 0;
            _dependencies.Clear();
            _totalDependencies = 0;
            Network.Instance.Client.NetworkNodeSpawned -= CheckDependencyAndInvokeCallback;
        }

        /// <summary>
        /// Should initialize this node from json data. Called when a remote client receives a spawn.
        /// </summary>
        protected internal abstract void SetJson(string json);

        /// <summary>
        /// Should return the current state of this node as json. Called when syncing to a new client.
        /// </summary>
        protected internal abstract string ToJson();

        /// <summary>
        /// Called when the server sends an update for this node. Override to handle incoming data.
        /// </summary>
        protected internal virtual void ReceiveData(ushort type, string json) { }

        /// <summary>
        /// Called when the server initializes this node. Override to run setup logic.
        /// </summary>
        protected virtual void OnNetworkInitialized() { }

        /// <summary>
        /// Called on every despawn, before network state is reset. Override to clean up
        /// state that should be fresh if the object is re-spawned.
        /// </summary>
        protected virtual void OnDespawned() { }

        /// <summary>
        /// Called once all dependencies added via <see cref="AddDependency{T}"/> have been initialized.
        /// Only fires if at least one dependency was added.
        /// </summary>
        protected virtual void OnDependenciesInitialized() { }

        /// <summary>
        /// Override to customize version checking. Return true to always accept updates.
        /// </summary>
        protected internal virtual bool CheckAndHandleUpdateVersion(ushort type, uint version) => true;
    }
}
