using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

public static class Gateway
{
    public static Socket listenfd;
    public static Dictionary<Socket, ClientState> states = new Dictionary<Socket, ClientState>();
    public static List<Socket> sockets = new List<Socket>();
    private static float pingInterval = 2;

    public static void Connect(string ip,int port)
    {
        listenfd = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        IPAddress iPAddress = IPAddress.Parse(ip);
        IPEndPoint iPEndPoint = new IPEndPoint(iPAddress, port);
        listenfd.Bind(iPEndPoint);
        listenfd.Listen(0);

        Console.WriteLine("�����������ɹ�");
        while (true)
        {
            sockets.Clear();
            //�ŷ���˵�Socket
            sockets.Add(listenfd);
            //�ſͻ��˵�Socket
            foreach (Socket socket in states.Keys)
            {
                sockets.Add(socket);
            }
            Socket.Select(sockets,null,null,1000);
            for (int i = 0; i < sockets.Count; i++)
            {
                Socket s = sockets[i];
                if(s == listenfd)
                {
                    //�пͻ���Ҫ����
                    Accept(s);
                }
                else
                {
                    //�ͻ��˷���Ϣ������
                    Receive(s);
                }
            }
            CheckPing();
        }
    }

    /// <summary>
    /// ����
    /// </summary>
    /// <param name="listenfd"></param>
    private static void Accept(Socket listenfd)
    {
        try
        {
            Socket socket = listenfd.Accept();
            Console.WriteLine("Accept " + socket.RemoteEndPoint.ToString());
            //���������ͻ��˵Ķ���
            ClientState state = new ClientState();
            state.socket = socket;

            state.lastPingTime = GetTimeStamp();

            states.Add(socket,state);
        }
        catch (SocketException e)
        {

            Console.WriteLine("Accept ʧ��" + e.Message);
        }
    }

    private static void Receive(Socket socket)
    {
        ClientState state = states[socket];
        ByteArray readBuffer = state.readBuffer;

        if(readBuffer.Remain <= 0)
        {
            readBuffer.MoveBytes();
        }
        if (readBuffer.Remain <= 0)
        {
            Console.WriteLine("Receive ʧ�ܣ����鲻����");
            Close(state);
            return;
        }
        int count = 0;
        try
        {
            count = socket.Receive(readBuffer.bytes, readBuffer.writeIndex, readBuffer.Remain,0);
        }
        catch (SocketException e)
        {

            Console.WriteLine("Rceive ʧ�ܣ�"+e.Message);
            Close(state);
            return;
        }

        if(count <= 0)
        {
            Console.WriteLine("Socket Close:"+socket.RemoteEndPoint.ToString());
            Close(state);
            return;
        }

        readBuffer.writeIndex += count;
        OnReceiveData(state);
    }

    /// <summary>
    /// ������Ϣ
    /// </summary>
    /// <param name="state"></param>
    private static void OnReceiveData(ClientState state)
    {
        ByteArray readBuffer = state.readBuffer;
        byte[] bytes = readBuffer.bytes;
        int readIndex = readBuffer.readIndex;

        if(readBuffer.Length <= 2)
            return;

        //�����ܳ���
        short length = (short)(bytes[readIndex + 1] * 256 + bytes[readIndex]);
        //���յ���Ϣû�н��������Ķ�
        if(readBuffer.Length < length)
            return;
        readBuffer.readIndex += 2;

        int nameCount = 0;
        string protoName = MsgBase.DecodeName(readBuffer.bytes, readBuffer.readIndex, out nameCount);
        if(protoName == "")
        {
            Console.WriteLine("OnReceiveData ʧ�ܣ�Э����Ϊ��");
            Close(state);
            return;
        }
        readBuffer.readIndex += nameCount;

        int bodyLength = length - nameCount;
        MsgBase msgBase = MsgBase.Decode(protoName, readBuffer.bytes, readBuffer.readIndex, bodyLength);

        readBuffer.readIndex += bodyLength;
        readBuffer.MoveBytes();

        /*//���Խ��տͻ�����Ϣ��ͬʱ������Ϣ���ͻ���
        MsgTest msg = (MsgTest)msgBase;
        Console.WriteLine("����˽������ݣ�" + msg.protoName);
        Send(state, msg);*/

        //ͨ��������ÿͻ��˷�������Э���Ӧ�ķ���
        MethodInfo mi = typeof(MsgHandler).GetMethod(protoName);
        Console.WriteLine("Receive:" + protoName);
        if(mi != null)
        {
            //Ҫִ�з����Ĳ���
            object[] o = { state, msgBase };
            mi.Invoke(null, o);
        }
        else
        {
            Console.WriteLine("OnReceiveData ����ʧ��");
        }

        if(readBuffer.Length > 2)
        {
            OnReceiveData(state);
        }
    }

    /// <summary>
    /// ������Ϣ
    /// </summary>
    /// <param name="state"></param>
    /// <param name="msgBase"></param>
    public static void Send(ClientState state,MsgBase msgBase)
    {
        if (state == null || !state.socket.Connected)
            return;

        //����
        byte[] nameBytes = MsgBase.EncodeName(msgBase);
        byte[] bodyBytes = MsgBase.Encode(msgBase);
        int len = nameBytes.Length + bodyBytes.Length;
        byte[] sendBytes = new byte[len + 2];
        sendBytes[0] = (byte)(len % 256);
        sendBytes[1] = (byte)(len / 256);
        Array.Copy(nameBytes, 0, sendBytes, 2, nameBytes.Length);
        Array.Copy(bodyBytes, 0, sendBytes, 2 + nameBytes.Length, bodyBytes.Length);

        try
        {
            state.socket.Send(sendBytes, 0, sendBytes.Length, 0);
        }
        catch (SocketException e)
        {

            Console.WriteLine("Send ʧ�ܣ�"+e.Message);
        }
    }

    private static void Close(ClientState state)
    {
        state.socket.Close();
        states.Remove(state.socket);
    }

    /// <summary>
    /// ��ȡʱ���
    /// </summary>
    /// <returns></returns>
    public static long GetTimeStamp()
    {
        TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        return Convert.ToInt64(ts.TotalSeconds);
    }

    private static void CheckPing()
    {
        foreach (ClientState state in states.Values)
        {
            if(GetTimeStamp() - state.lastPingTime > pingInterval * 4)
            {
                Console.WriteLine("�������ƣ��Ͽ����ӣ�",state.socket.RemoteEndPoint);
                //�رտͻ���
                Close(state);
                return;
            }
        }
    }
}

/*//����Э��
public class MsgTest : MsgBase
{
    public MsgTest()
    {
        protoName = "MsgTest";
    }
}*/