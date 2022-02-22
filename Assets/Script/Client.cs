using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System;

public class Client : MonoBehaviour
{
    public static Client instance;
    public static int dataBufferSize = 4096;

    public string ip = "127.0.0.1";
    public int port = 26950;
    public int myId = 0;
    public TCP tcp;
    public UDP udp;

    private delegate void PacketHandler(Packet _packet);
    private static Dictionary<int, PacketHandler> packetHandlers;

    private void Awake()
    {
        if(instance == null)
        {
            instance = this;
        }
        else if(instance != this)
        {
            Debug.Log("Instance already exist, destroying object!");
            Destroy(this);
        }
    }

    private void Start()
    {
        tcp = new TCP();
        udp = new UDP();
    }

    public void ConnectToServer()
    {
        InitializeClientData();

        tcp.Connect();
    }

    public class TCP
    {
        public TcpClient socket;

        private NetworkStream stream;
        private Packet receiveData;
        private byte[] receiveBuffer;

        public void Connect()
        {
            
            socket = new TcpClient
            {
               
                ReceiveBufferSize = dataBufferSize,
                SendBufferSize = dataBufferSize
            };
            
            receiveBuffer = new byte[dataBufferSize];
            /*BeginConnect는 원격 호스트 연결에 대한 비동기 요청, 
             ConnectCallback은 작업이 완료되었을 때 호출
             socket은 연결 작업에 대한 정보, 완료되면 ConnectCallback에 전달*/
            socket.BeginConnect(instance.ip, instance.port, ConnectCallback, socket);

        }

        /*IAsyncResult인터페이스 : 비동기 작업 상태를 나타냄*/
        private void ConnectCallback(IAsyncResult _result)
        {
            /*EndConnect는 보류 중인 비동기 연결 시도를 끝냄*/
            socket.EndConnect(_result);
            
            /*socket.Connected는 TcpClient이 원격 호스트에 연결되어 있는지 여부확인*/
            if(!socket.Connected)
            {
                return;
            }

            /*GetStream 데이터를 보내고 받는데 사용되는 NetworkStream을 반환*/
            stream = socket.GetStream();
            
            receiveData = new Packet();
            /*BeginRead는 비동기 읽기를 시작
             receiveBuffer는 읽은 데이터를 저장하기 위한 메모리
             0은 데이터를 저장하기 시작하는 buffer 내의 위치
             dataBufferSize는 읽을 바이트 수
             BeginRead가 완료되었을 때 콜백*/
            stream.BeginRead(receiveBuffer, 0, dataBufferSize, ReceiveCallback, null);
        }

        public void SendData(Packet _packet)
        {
            try
            {
                if(socket != null)
                {
                    /*BeginWrite는 비동기 쓰기를 시작
                     _packet.ToArray()는 쓸 데이터를 포함하는 NetworkSTream형식의 배열
                     데이터를 보내기 시작하는 buffer내 위치
                     _packet.Length()는 NetworkStream의 쓸 바이트 수
                     null은 완료되고 실행되는 콜백
                     null은 사용자 정의 데이터*/
                    stream.BeginWrite(_packet.ToArray(), 0, _packet.Length(), null, null);
                }
            }
            catch(Exception _ex)
            {
                Debug.Log($"Error sending data to sever via TCP: {_ex}");
            }
        }

        private void ReceiveCallback(IAsyncResult _result)
        {
            try
            {
                /*EndRead는 비동기 읽기의 끝을 처리
                 NetworkStream에서 읽은 바이트 수*/
                int _byteLength = stream.EndRead(_result);
                if(_byteLength <= 0)
                {
                    return;
                }

                byte[] _data = new byte[_byteLength];
                /*Array의 요소 범위를 다른 Array에 복사하고 필요에 따라 형식 캐스팅 및 
                 Boxing을 수행
                 
                receiveBuffer는 읽은 데이터를 저장한 변수
                _data는 다른 Array에 저장할 배열
                _byteLength는 저장할 길이
                 */
                Array.Copy(receiveBuffer, _data, _byteLength);


                receiveData.Reset(HandleData(_data));
                /*BeginRead는 비동기 읽기를 시작
                 receiveBuffer는 읽은 데이터를 저장하기 위한 메모리
                 0은 데이터를 저장하기 시작하는 buffer 내의 위치
                 dataBufferSize는 읽을 바이트 수
                 BeginRead가 완료되었을 때 콜백*/
                //stream.BeginRead(receiveBuffer, 0, dataBufferSize, ReceiveCallback,null);
            }
            catch
            {

            }
        }

        private bool HandleData(byte[] _data)
        {
            int _packetLength = 0;

            /*
            public void SetBytes(byte[] _data)
                {
                      Write(_data);//리스트에 전달받은 데이터 저장
                      readableBuffer = buffer.ToArray(); //리스트를 배열로 저장
                 }

            public void Write(byte[] _value)
            {
                buffer.AddRange(_value);
            }

             */
            receiveData.SetBytes(_data);
            if(receiveData.UnreadLength() >= 4)
            {
                _packetLength = receiveData.ReadInt();
                if(_packetLength <= 0)
                {
                    return true;
                }
            }

            while(_packetLength > 0 && _packetLength <= receiveData.UnreadLength())
            {
                byte[] _packetBytes = receiveData.ReadBytes(_packetLength);
                ThreadManager.ExecuteOnMainThread(() =>
                {
                    using (Packet _packet = new Packet(_packetBytes))
                    {
                        int _packetId = _packet.ReadInt();
                        packetHandlers[_packetId](_packet);
                    }

                });
                _packetLength = 0;
                if (receiveData.UnreadLength() >= 4)
                {
                    _packetLength = receiveData.ReadInt();
                    if (_packetLength <= 0)
                    {
                        return true;
                    }
                }
            }

            if(_packetLength <= 1)
            {
                return true;
            }
            return false;
        }
    }

    public class UDP
    {
        public UdpClient socket;
        public IPEndPoint endPoint;

        public UDP()
        {
            endPoint = new IPEndPoint(IPAddress.Parse(instance.ip), instance.port);
        }

        public void Connect(int _localPort)
        {
            socket = new UdpClient(_localPort);

            socket.Connect(endPoint);
            socket.BeginReceive(ReceiveCallback, null);

            using (Packet _packet = new Packet())
            {
                SendData(_packet);
            }
        }

        public void SendData(Packet _packet)
        {
            try
            {
                _packet.InsertInt(instance.myId);
                if(socket != null)
                {
                    socket.BeginSend(_packet.ToArray(), _packet.Length(), null, null);
                }
            }
            catch (Exception _ex)
            {
                Debug.Log($"Error sending data to server via UDP: {_ex}");
            }
        }

        private void ReceiveCallback(IAsyncResult _result)
        {
            try
            {
                byte[] _data = socket.EndReceive(_result, ref endPoint);
                socket.BeginReceive(ReceiveCallback, null);

                if(_data.Length < 4)
                {
                    return;
                }

                HandleData(_data);
            }
            catch
            {

            }
        }

        private void HandleData(byte[] _data)
        {
            using (Packet _packet = new Packet(_data))
            {
                int _packetLength = _packet.ReadInt();
                _data = _packet.ReadBytes(_packetLength);
            }

            ThreadManager.ExecuteOnMainThread(() =>
            {
                using (Packet _packet = new Packet(_data))
                {
                    int _packetId = _packet.ReadInt();
                    packetHandlers[_packetId](_packet);
                }
            });
        }
    }

    private void InitializeClientData()
    {
        packetHandlers = new Dictionary<int, PacketHandler>()
        {
            { (int)ServerPackets.welcome,ClientHandle.Welcome},
            { (int)ServerPackets.udpTest,ClientHandle.UDPTest}
        };
        Debug.Log("Initalized packets.");
    }
}
