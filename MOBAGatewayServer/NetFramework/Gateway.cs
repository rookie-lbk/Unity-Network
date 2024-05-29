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

        Console.WriteLine("服务器启动成功");
        while (true)
        {
            sockets.Clear();
            //放服务端的Socket
            sockets.Add(listenfd);
            //放客户端的Socket
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
                    //有客户端要连接
                    Accept(s);
                }
                else
                {
                    //客户端发消息过来了
                    Receive(s);
                }
            }
            CheckPing();
        }
    }

    /// <summary>
    /// 接收
    /// </summary>
    /// <param name="listenfd"></param>
    private static void Accept(Socket listenfd)
    {
        try
        {
            Socket socket = listenfd.Accept();
            Console.WriteLine("Accept " + socket.RemoteEndPoint.ToString());
            //创建描述客户端的对象
            ClientState state = new ClientState();
            state.socket = socket;

            state.lastPingTime = GetTimeStamp();

            states.Add(socket,state);
        }
        catch (SocketException e)
        {

            Console.WriteLine("Accept 失败" + e.Message);
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
            Console.WriteLine("Receive 失败，数组不够大");
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

            Console.WriteLine("Rceive 失败，"+e.Message);
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
    /// 处理消息
    /// </summary>
    /// <param name="state"></param>
    private static void OnReceiveData(ClientState state)
    {
        ByteArray readBuffer = state.readBuffer;
        byte[] bytes = readBuffer.bytes;
        int readIndex = readBuffer.readIndex;

        if(readBuffer.Length <= 2)
            return;

        //解析总长度
        short length = (short)(bytes[readIndex + 1] * 256 + bytes[readIndex]);
        //接收的消息没有解析出来的多
        if(readBuffer.Length < length)
            return;
        readBuffer.readIndex += 2;

        int nameCount = 0;
        string protoName = MsgBase.DecodeName(readBuffer.bytes, readBuffer.readIndex, out nameCount);
        if(protoName == "")
        {
            Console.WriteLine("OnReceiveData 失败，协议名为空");
            Close(state);
            return;
        }
        readBuffer.readIndex += nameCount;

        int bodyLength = length - nameCount;
        MsgBase msgBase = MsgBase.Decode(protoName, readBuffer.bytes, readBuffer.readIndex, bodyLength);

        readBuffer.readIndex += bodyLength;
        readBuffer.MoveBytes();

        /*//测试接收客户端消息，同时发送消息给客户端
        MsgTest msg = (MsgTest)msgBase;
        Console.WriteLine("服务端接收内容：" + msg.protoName);
        Send(state, msg);*/

        //通过反射调用客户端发过来的协议对应的方法
        MethodInfo mi = typeof(MsgHandler).GetMethod(protoName);
        Console.WriteLine("Receive:" + protoName);
        if(mi != null)
        {
            //要执行方法的参数
            object[] o = { state, msgBase };
            mi.Invoke(null, o);
        }
        else
        {
            Console.WriteLine("OnReceiveData 反射失败");
        }

        if(readBuffer.Length > 2)
        {
            OnReceiveData(state);
        }
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    /// <param name="state"></param>
    /// <param name="msgBase"></param>
    public static void Send(ClientState state,MsgBase msgBase)
    {
        if (state == null || !state.socket.Connected)
            return;

        //编码
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

            Console.WriteLine("Send 失败："+e.Message);
        }
    }

    private static void Close(ClientState state)
    {
        state.socket.Close();
        states.Remove(state.socket);
    }

    /// <summary>
    /// 获取时间戳
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
                Console.WriteLine("心跳机制，断开连接：",state.socket.RemoteEndPoint);
                //关闭客户端
                Close(state);
                return;
            }
        }
    }
}

/*//测试协议
public class MsgTest : MsgBase
{
    public MsgTest()
    {
        protoName = "MsgTest";
    }
}*/