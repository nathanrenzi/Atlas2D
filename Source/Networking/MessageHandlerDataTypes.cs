using Riptide;

namespace Atlas2D.Networking
{
    internal struct NetworkNodeData
    {
        public uint Version;
        public int RegisterID;
        public string NetworkID;
        public ushort OwnerID;
        public string Json;
    }
    internal struct NetworkNodeUpdatedData
    {
        public MessageSendMode MessageSendMode;
        public string NetworkID;
        public uint Version;
        public ushort Type;
        public string Json;
    }

    internal struct NetworkNodeSyncSpawnData
    {
        public uint Version;
        public int RegisterID;
        public string NetworkID;
        public ushort ClientID;
        public string Json;
    }

    internal struct NetworkGameClassRequestSyncData
    {
        public ushort ToClient;
        public int TotalCount;
        public NetworkNodeData[] GameClasses;
    }
}
