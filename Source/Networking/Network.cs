using Riptide;
using Riptide.Utils;
using Atlas2D.Networking.Core;
using Steamworks;
using System.Reflection;

namespace Atlas2D.Networking
{
    /// <summary>
    /// A general purpose networking class powered by <see href="https://riptide.tomweiland.net/manual/overview/get-started.html">RiptideNetworking</see>.
    /// </summary>
    public sealed class Network : IGameClass
    {
        public delegate NetworkNode CreateNetworkNode(string json);
        public delegate void SetNetworkNodeRegisterID(int registerID);
        public static List<CreateNetworkNode> NetworkNodeRegister { get; private set; } = new();
        /// <summary>
        /// Register IDs of all <see cref="NetworkNode"/> types marked with <see cref="HostOnlyAttribute"/>.
        /// Populated by <see cref="RegisterAll"/>. The server uses this to reject spawns from non-host clients.
        /// </summary>
        public static HashSet<int> HostOnlyRegisterIDs { get; private set; } = new();

        public static Network Instance { get; } = new();

        public Core.Server Server { get; private set; }
        public Core.Client Client { get; private set; }

        public TransportMode TransportMode { get; private set; }

        public const ushort MIN_MESSAGE_TYPE_INTERCEPT = 60000;
        public const ushort MAX_MESSAGE_TYPE_INTERCEPT = 60004;

        /// <summary>
        /// The largest JSON (in UTF-8 bytes) a <see cref="NetworkNode"/> can spawn, sync or send with.
        /// A Riptide message holds about 1225 bytes, and the rest of the message needs room too.
        /// </summary>
        public const int MAX_NODE_JSON_BYTES = 1000;
        /// <summary>
        /// The longest NetworkID a <see cref="NetworkNode"/> can have.
        /// </summary>
        public const int MAX_NETWORK_ID_LENGTH = 64;

        private bool _initialized = false;

        /// <summary>
        /// Initializes network functionality. Networking must be initialized before it can be used.
        /// </summary>
        public void Initialize(TransportMode transportMode)
        {
            if (_initialized)
            {
                Debug.LogError("Networking is already initialized, cannot initialize again without resetting!");
                return;
            }

            switch (transportMode)
            {
                case TransportMode.NetworkP2P:
                    InitializeForNetwork();
                    break;
                case TransportMode.SteamP2P:
                    InitializeForSteam();
                    break;
                default:
                    break;
            }
        }

        private void InitializeForNetwork()
        {
            _initialized = true;
            TransportMode = TransportMode.NetworkP2P;
            RiptideLogger.Initialize(Debug.Log, Debug.Log, Debug.Log, Debug.LogError, false);
            Client = new Core.Client(TransportMode);
            Server = new Core.Server(TransportMode);
            Client.SetServer(Server);
            Engine.Game.RegisterGameClass(this);
        }

        private void InitializeForSteam()
        {
            _initialized = true;
            TransportMode = TransportMode.SteamP2P;
            uint steamAppID;
            if(File.Exists("steam_appid.txt"))
            {
                steamAppID = uint.Parse(File.ReadAllText("steam_appid.txt"));
            }
            else
            {
                Debug.LogError("[Steamworks.NET] steam_appid.txt cannot be found.");
                return;
            }

            Debug.Log("\n");
            if (!Packsize.Test())
                Debug.LogError("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.");
            if (!DllCheck.Test())
                Debug.LogError("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.");
            try
            {
                if (SteamAPI.RestartAppIfNecessary((AppId_t)steamAppID))
                {
                    Debug.Log("[Steamworks.NET] Game was not started through steam. Restarting...");
                    Engine.Game.Exit();
                }
            }
            catch (DllNotFoundException e)
            {
                Debug.LogError("[Steamworks.NET] Could not load steam_api.dll.\n" + e.Message);
                Engine.Game.Exit();
            }
            if (!SteamAPI.Init())
            {
                Debug.LogError("[Steamworks.NET] Steam API could not be initialized.");
                Engine.Game.Exit();
            }
            RiptideLogger.Initialize(Debug.Log, Debug.Log, Debug.Log, Debug.LogError, false);

            Client = new Core.Client(TransportMode);
            Server = new Core.Server(TransportMode);
            Client.SetServer(Server);
            Engine.Game.RegisterGameClass(this);
        }

        private static int RegisterNetworkNode(CreateNetworkNode factoryMethod)
        {
            if (!NetworkNodeRegister.Contains(factoryMethod))
            {
                NetworkNodeRegister.Add(factoryMethod);
                return NetworkNodeRegister.Count - 1;
            }
            return -1;
        }

        public static void RegisterAll()
        {
            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.FullName.StartsWith("System") && !a.FullName.StartsWith("Microsoft"))
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch (ReflectionTypeLoadException e)
                    {
                        return e.Types.Where(t => t != null)!;
                    }
                })
                .Where(t => t.IsClass && !t.IsAbstract)
                .ToList();

            // Sorted by FullName to ensure deterministic ID assignment across runs
            var networkNodes = allTypes
                .Where(t => typeof(NetworkNode).IsAssignableFrom(t))
                .OrderBy(t => t.FullName)
                .ToList();

            var networkServices = allTypes
                .Where(t => typeof(NetworkService).IsAssignableFrom(t))
                .ToList();

            foreach (var type in networkNodes)
            {
                if (!typeof(INetworkFactory).IsAssignableFrom(type))
                {
                    Debug.LogWarning($"[Network] {type.FullName} is a NetworkNode but does not implement INetworkFactory");
                    continue;
                }

                var method = type.GetMethod("FactoryMethod", BindingFlags.Static | BindingFlags.Public)
                    ?? throw new InvalidOperationException($"{type.FullName} must implement INetworkFactory to be registered.");
                var del = (CreateNetworkNode)Delegate.CreateDelegate(typeof(CreateNetworkNode), method);
                int id = RegisterNetworkNode(del);
                NetworkNode.AssignRegisterID(type, id);
                if (type.IsDefined(typeof(HostOnlyAttribute), inherit: true))
                {
                    HostOnlyRegisterIDs.Add(id);
                    Debug.Log($"[Network] NetworkNode registered (host-only): {type.FullName} => ID {id}");
                }
                else
                {
                    Debug.Log($"[Network] NetworkNode registered: {type.FullName} => ID {id}");
                }
            }

            foreach (var type in networkServices)
            {
                Instance.Client.Services.Register(type);
                Instance.Server.Services.Register(type);
                Debug.Log($"[Network] NetworkService registered: {type.FullName}");
            }
        }

        public void Update()
        {
            if(TransportMode == TransportMode.SteamP2P) SteamAPI.RunCallbacks();
            if (Server != null) Server.ServerUpdate();
            Client.ClientUpdate();
        }

        public void FixedUpdate() { }

        public void Dispose()
        {
            Client.Dispose();
            NetworkNodeRegister.Clear();
            NetworkNodeRegister = null;
            if(TransportMode == TransportMode.SteamP2P) SteamAPI.Shutdown();
            Engine.Game.UnregisterGameClass(this);
        }

        /// <summary>
        /// Resets the NetworkManager so that it can be reinitialized.
        /// </summary>
        public void Reset()
        {
            Client.Dispose();
            Server.Stop();
            NetworkNodeRegister.Clear();
            if (TransportMode == TransportMode.SteamP2P)
                Server.SteamServer.Shutdown();
            Server = null;
            _initialized = false;
        }
    }
}
