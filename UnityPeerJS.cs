using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class UnityPeerJS
{
    private enum EventType
    {
        None = 0,
        Initialized = 1,
        Connected = 2,
        Received = 3,
        ConnClosed = 4,
        PeerDisconnected = 5,
        PeerClosed = 6,
        Error = 7
    }

    public class Peer
    {
        private readonly Dictionary<int, Connection> _connections = new Dictionary<int, Connection>();

        private readonly int _peerIndex;

        public Peer(string key, string id, string host, int port)
        {
            Init();

            _peerIndex = OpenPeer(key, id, host, port);
        }

        public event Action OnOpen;
        public event Action<IConnection> OnConnection;
        public event Action OnDisconnected;
        public event Action OnClose;
        public event Action<string> OnError;

        public void Pump()
        {
            EventType eventType;
            while ((eventType = (EventType) NextEventType(_peerIndex)) != EventType.None)
            {
                switch (eventType)
                {
                    case EventType.Initialized:
                    {
                        PopInitializedEvent(_peerIndex);

                        if (OnOpen != null)
                            OnOpen();

                        break;
                    }
                    case EventType.Connected:
                    {
                        var buffer = new byte[256];
                        var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                        var connIndex = PopConnectedEvent(_peerIndex, pinnedBuffer.AddrOfPinnedObject(), buffer.Length);

                        pinnedBuffer.Free();

                        var remoteId = DecodeUtf16Z(buffer);

                        _connections[connIndex] = new Connection(this, connIndex, remoteId);

                        if (OnConnection != null)
                            OnConnection(_connections[connIndex]);

                        break;
                    }
                    case EventType.Received:
                    {
                        var size = PeekReceivedEventSize(_peerIndex);

                        var buffer = new byte[size];
                        var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                        var connIndex = PopReceivedEvent(_peerIndex, pinnedBuffer.AddrOfPinnedObject(), size);

                        pinnedBuffer.Free();

                        _connections[connIndex].EmitOnData(DecodeUtf16Z(buffer));
                        break;
                    }

                    case EventType.ConnClosed:
                    {
                        var connIndex = PopConnClosedEvent(_peerIndex);

                        _connections[connIndex].EmitOnClose();
                        break;
                    }

                    case EventType.PeerDisconnected:
                    {
                        PopPeerDisconnectedEvent(_peerIndex);

                        if (OnDisconnected != null)
                            OnDisconnected();

                        break;
                    }

                    case EventType.PeerClosed:
                    {
                        PopPeerClosedEvent(_peerIndex);

                        if (OnClose != null)
                            OnClose();

                        break;
                    }

                    case EventType.Error:
                    {
                        var buffer = new byte[256];
                        var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                        PopErrorEvent(_peerIndex, pinnedBuffer.AddrOfPinnedObject(), buffer.Length);

                        pinnedBuffer.Free();

                        if (OnError != null)
                            OnError(DecodeUtf16Z(buffer));

                        break;
                    }

                    default:
                    {
                        PopAnyEvent(_peerIndex);
                        break;
                    }
                }
            }
        }

        public void Connect(string remoteId)
        {
            UnityPeerJS.Connect(_peerIndex, remoteId);
        }

        public void Disconnect()
        {
            PeerDisconnect(_peerIndex);
        }

        public void Destroy()
        {
            PeerDestroy(_peerIndex);
        }

        private string DecodeUtf16Z(byte[] buffer)
        {
            var length = 0;
            while (length + 1 < buffer.Length && (buffer[length] != 0 || buffer[length + 1] != 0))
                length += 2;
            return Encoding.Unicode.GetString(buffer, 0, length);
        }

        public interface IConnection
        {
            Peer Peer { get; }

            string RemoteId { get; }

            event Action<string> OnData;
            event Action OnClose;

            void Send(string str);
            void Close();
        }

        private class Connection : IConnection
        {
            private readonly int _connIndex;

            public Connection(Peer peer, int connIndex, string remoteId)
            {
                Peer = peer;
                _connIndex = connIndex;
                RemoteId = remoteId;
            }

            public event Action<string> OnData;
            public event Action OnClose;

            public Peer Peer { get; set; }
            public string RemoteId { get; set; }

            public void Send(string str)
            {
                UnityPeerJS.Send(Peer._peerIndex, _connIndex, str, str.Length);
            }

            public void Close()
            {
                ConnClose(Peer._peerIndex, _connIndex);
            }

            public void EmitOnData(string str)
            {
                if (OnData != null)
                    OnData(str);
            }

            public void EmitOnClose()
            {
                if (OnClose != null)
                    OnClose();
            }
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR

    [DllImport("__Internal")]
    private static extern void Init();

    [DllImport("__Internal")]
    private static extern int OpenPeer(string key, string id, string hostStr, int port);

    [DllImport("__Internal")]
    private static extern void Connect(int peerInstance, string remoteId);

    [DllImport("__Internal")]
    private static extern void Send(int peerInstance, int connInstance, string ptr, int length);

    [DllImport("__Internal")]
    private static extern void ConnClose(int peerInstance, int connInstance);

    [DllImport("__Internal")]
    private static extern void PeerDisconnect(int peerInstance);

    [DllImport("__Internal")]
    private static extern void PeerDestroy(int peerInstance);

    [DllImport("__Internal")]
    private static extern int NextEventType(int peerInstance);

    [DllImport("__Internal")]
    private static extern void PopAnyEvent(int peerInstance);

    [DllImport("__Internal")]
    private static extern void PopInitializedEvent(int peerInstance);

    [DllImport("__Internal")]
    private static extern int PopConnectedEvent(int peerInstance, IntPtr remoteIdPtr, int remoteIdMaxLength);

    [DllImport("__Internal")]
    private static extern int PeekReceivedEventSize(int peerInstance);

    [DllImport("__Internal")]
    private static extern int PopReceivedEvent(int peerInstance, IntPtr dataPtr, int dataMaxLength);

    [DllImport("__Internal")]
    private static extern int PopConnClosedEvent(int peerInstance);

    [DllImport("__Internal")]
    private static extern void PopPeerDisconnectedEvent(int peerInstance);

    [DllImport("__Internal")]
    private static extern void PopPeerClosedEvent(int peerInstance);

    [DllImport("__Internal")]
    private static extern void PopErrorEvent(int peerInstance, IntPtr errorPtr, int errorMaxLength);
#else

    // ReSharper disable UnusedParameter.Local
    private static void Init()
    {
        throw new NotImplementedException();
    }

    private static int OpenPeer(string keyStr, string idStr, string hostStr, int port)
    {
        throw new NotImplementedException();
    }

    private static void Connect(int peerInstance, string remoteIdStr)
    {
        throw new NotImplementedException();
    }

    private static void Send(int peerInstance, int connInstance, string ptr, int length)
    {
        throw new NotImplementedException();
    }

    private static void ConnClose(int peerInstance, int connInstance)
    {
        throw new NotImplementedException();
    }

    private static void PeerDisconnect(int peerInstance)
    {
        throw new NotImplementedException();
    }

    private static void PeerDestroy(int peerInstance)
    {
        throw new NotImplementedException();
    }

    private static int NextEventType(int peerInstance)
    {
        throw new NotImplementedException();
    }

    private static void PopAnyEvent(int peerInstance)
    {
        throw new NotImplementedException();
    }

    private static void PopInitializedEvent(int peerInstance)
    {
        throw new NotImplementedException();
    }

    private static int PopConnectedEvent(int peerInstance, IntPtr remoteIdPtr, int remoteIdMaxLength)
    {
        throw new NotImplementedException();
    }

    private static int PeekReceivedEventSize(int peerInstance)
    {
        throw new NotImplementedException();
    }

    private static int PopReceivedEvent(int peerInstance, IntPtr dataPtr, int dataMaxLength)
    {
        throw new NotImplementedException();
    }

    private static int PopConnClosedEvent(int peerInstance)
    {
        throw new NotImplementedException();
    }

    private static void PopPeerDisconnectedEvent(int peerInstance)
    {
        throw new NotImplementedException();
    }

    private static void PopPeerClosedEvent(int peerInstance)
    {
        throw new NotImplementedException();
    }

    private static void PopErrorEvent(int peerInstance, IntPtr errorPtr, int errorMaxLength)
    {
        throw new NotImplementedException();
    }
#endif
}