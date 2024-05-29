using ProtoBuf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using UnityEngine;

public static class NetManager
{
    /// <summary>
    /// 客户端套接字
    /// </summary>
    private static Socket socket;

    /// <summary>
    /// 字节数组
    /// </summary>
    private static ByteArray byteArray;

    /// <summary>
    /// 消息列表
    /// </summary>
    private static List<IExtensible> msgList;

    /// <summary>
    /// 是否正在连接
    /// </summary>
    private static bool isConnecting;

    /// <summary>
    /// 是否正在关闭
    /// </summary>
    private static bool isClosing;

    /// <summary>
    /// 发送队列
    /// </summary>
    private static Queue<ByteArray> writeQueue;

    /// <summary>
    /// 一帧处理的最大消息量
    /// </summary>
    private static int processMsgCount = 10;

    /// <summary>
    /// 是否启用心跳机制
    /// </summary>
    private static bool isUsePing = true;

    /// <summary>
    /// 上一次发送Ping的时间
    /// </summary>
    private static float lastPingTime = 0;

    /// <summary>
    /// 上一次收到Pong的时间
    /// </summary>
    private static float lastPongTime = 0;

    /// <summary>
    /// 心跳机制的时间间隔
    /// </summary>
    private static float pingInterval = 2;

    /// <summary>
    /// 网络事件
    /// </summary>
    public enum NetEvent
    {
        ConnectSucc = 1,
        ConnectFail = 2,
        Close,
    }

    /// <summary>
    /// 执行的事件
    /// </summary>
    /// <param name="err"></param>
    public delegate void EventListener(string err);

    /// <summary>
    /// 事件的字典
    /// </summary>
    private static Dictionary<NetEvent, EventListener> eventListener = new Dictionary<NetEvent, EventListener>();

    /// <summary>
    /// 添加事件
    /// </summary>
    /// <param name="netEvent"></param>
    /// <param name="listener"></param>
    public static void AddEventListener(NetEvent netEvent, EventListener listener)
    {
        if (eventListener.ContainsKey(netEvent))
        {
            eventListener[netEvent] += listener;
        }
        else
        {
            eventListener.Add(netEvent, listener);
        }
    }

    /// <summary>
    /// 移除事件
    /// </summary>
    /// <param name="netEvent"></param>
    /// <param name="listener"></param>
    public static void RemoveListener(NetEvent netEvent, EventListener listener)
    {
        if (eventListener.ContainsKey(netEvent))
        {
            eventListener[netEvent] -= listener;
            if (eventListener[netEvent] == null)
            {
                eventListener.Remove(netEvent);
            }
        }
    }

    /// <summary>
    /// 分发事件
    /// </summary>
    /// <param name="netEvent"></param>
    /// <param name="err"></param>
    public static void FireEvent(NetEvent netEvent, string err)
    {
        if (eventListener.ContainsKey(netEvent))
        {
            eventListener[netEvent](err);
        }
    }

    /// <summary>
    /// 消息处理委托
    /// </summary>
    /// <param name="msgBase">消息</param>
    public delegate void MsgListener(IExtensible msgBase);

    /// <summary>
    /// 消息事件字典
    /// </summary>
    private static Dictionary<string, MsgListener> msgListeners = new Dictionary<string, MsgListener>();

    /// <summary>
    /// 添加事件
    /// </summary>
    /// <param name="msgName">事件名字</param>
    /// <param name="listener"></param>
    public static void AddMsgListener(string msgName, MsgListener listener)
    {
        if (msgListeners.ContainsKey(msgName))
        {
            msgListeners[msgName] += listener;
        }
        else
        {
            msgListeners.Add(msgName, listener);
        }
    }

    /// <summary>
    /// 移除消息事件
    /// </summary>
    /// <param name="msgName">消息名</param>
    /// <param name="listener"></param>
    public static void RemoveMsgListener(string msgName, MsgListener listener)
    {
        if (msgListeners.ContainsKey(msgName))
        {
            msgListeners[msgName] -= listener;
            if (msgListeners[msgName] == null)
            {
                msgListeners.Remove(msgName);
            }
        }
    }

    /// <summary>
    /// 分发事件
    /// </summary>
    /// <param name="msgName">事件名字</param>
    /// <param name="msgBase"></param>
    public static void FireMsg(string msgName, IExtensible msgBase)
    {
        if (msgListeners.ContainsKey(msgName))
        {
            msgListeners[msgName](msgBase);
        }
    }

    /// <summary>
    /// 初始化
    /// </summary>
    private static void Init()
    {
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        byteArray = new ByteArray();
        msgList = new List<IExtensible>();
        isConnecting = false;
        isClosing = false;
        writeQueue = new Queue<ByteArray>();

        lastPingTime = Time.time;
        lastPongTime = Time.time;

        if (!msgListeners.ContainsKey("MsgPong"))
        {
            msgListeners.Add("MsgPong", OnMsgPong);
        }
    }

    /// <summary>
    /// 连接
    /// </summary>
    /// <param name="ip">ip地址</param>
    /// <param name="port">端口号</param>
    public static void Connect(string ip, int port)
    {
        if (socket != null && socket.Connected)
        {
            Debug.Log("连接失败，已经连接过了");
            return;
        }

        if (isConnecting)
        {
            Debug.Log("连接失败，正在连接");
            return;
        }

        Init();
        isConnecting = true;
        socket.BeginConnect(ip, port, ConnectCallback, socket);
    }

    /// <summary>
    /// 连接回调
    /// </summary>
    /// <param name="ar"></param>
    private static void ConnectCallback(IAsyncResult ar)
    {
        try
        {
            Socket socket = (Socket)ar.AsyncState;
            socket.EndConnect(ar);
            Debug.Log("连接成功");
            FireEvent(NetEvent.ConnectSucc, "");

            /*//测试向服务端发送消息
            MsgTest msg = new MsgTest();
            Send(msg);*/

            isConnecting = false;

            //接受消息
            socket.BeginReceive(byteArray.bytes, byteArray.writeIndex, byteArray.Remain, 0, ReceiveCallback, socket);
        }
        catch (SocketException e)
        {
            Debug.LogError("连接失败：" + e.Message);
            FireEvent(NetEvent.ConnectFail, e.Message);
            isConnecting = false;
        }
    }

    private static void ReceiveCallback(IAsyncResult ar)
    {
        try
        {
            Socket socket = (Socket)ar.AsyncState;
            //接收的数据量
            int count = socket.EndReceive(ar);
            //断开连接
            if (count == 0)
            {
                Close();
                return;
            }

            //接收数据
            byteArray.writeIndex += count;

            //处理消息
            OnReceiveData();
            //如果长度过小，扩容
            if (byteArray.Remain < 8)
            {
                byteArray.MoveBytes();
                byteArray.ReSize(byteArray.Length * 2);
            }

            socket.BeginReceive(byteArray.bytes, byteArray.writeIndex, byteArray.Remain, 0, ReceiveCallback, socket);
        }
        catch (SocketException e)
        {
            Debug.LogError("接收失败：" + e.Message);
        }
    }

    /// <summary>
    /// 关闭客户端
    /// </summary>
    private static void Close()
    {
        if (socket == null || !socket.Connected)
            return;
        if (isConnecting)
            return;
        //消息还没有发送完
        if (writeQueue.Count > 0)
        {
            isClosing = true;
        }
        else
        {
            socket.Close();
            FireEvent(NetEvent.Close, "");
        }
    }

    /// <summary>
    /// 处理接收过来的消息
    /// </summary>
    private static void OnReceiveData()
    {
        if (byteArray.Length <= 2)
            return;
        byte[] bytes = byteArray.bytes;
        int readIndex = byteArray.readIndex;
        //解析消息总体的长度
        short length = (short)(bytes[readIndex + 1] * 256 + bytes[readIndex]);


        if (byteArray.Length < length + 2)
            return;
        byteArray.readIndex += 2;
        int nameCount = 0;
        string protoName = ProtoBufTool.DecodeName(byteArray.bytes, byteArray.readIndex, out nameCount);
        if (protoName == "")
        {
            Debug.Log("协议名解析失败");
            return;
        }

        byteArray.readIndex += nameCount;


        //解析协议体
        int bodyLength = length - nameCount;
        IExtensible msgBase = ProtoBufTool.Decode(protoName, byteArray.bytes, byteArray.readIndex, bodyLength);
        byteArray.readIndex += bodyLength;

        Debug.Log("msg:" + protoName);
        //移动数据
        byteArray.MoveBytes();
        lock (msgList)
        {
            msgList.Add(msgBase);
        }

        if (byteArray.Length > 2)
        {
            OnReceiveData();
        }

        /*//测试接收服务端消息
        MsgTest msg = (MsgTest)msgBase;
        Debug.Log("客户端接收内容：" + msg.protoName);*/
    }

    /// <summary>
    /// 发送协议
    /// </summary>
    /// <param name="msg"></param>
    public static void Send(IExtensible msg)
    {
        if (socket == null || !socket.Connected)
            return;
        if (isConnecting)
            return;
        if (isClosing)
            return;

        //编码
        byte[] nameBytes = ProtoBufTool.EncodeName(msg);
        byte[] bodyBytes = ProtoBufTool.Encode(msg);
        int len = nameBytes.Length + bodyBytes.Length;
        byte[] sendBytes = new byte[len + 2];
        sendBytes[0] = (byte)(len % 256);
        sendBytes[1] = (byte)(len / 256);
        Array.Copy(nameBytes, 0, sendBytes, 2, nameBytes.Length);
        Array.Copy(bodyBytes, 0, sendBytes, 2 + nameBytes.Length, bodyBytes.Length);

        ByteArray ba = new ByteArray(sendBytes);
        int count = 0;
        lock (writeQueue)
        {
            writeQueue.Enqueue(ba);
            count = writeQueue.Count;
        }

        if (count == 1)
        {
            socket.BeginSend(sendBytes, 0, sendBytes.Length, 0, SendCallback, socket);
        }
    }

    /// <summary>
    /// 发送回调
    /// </summary>
    /// <param name="ar"></param>
    private static void SendCallback(IAsyncResult ar)
    {
        Socket socket = (Socket)ar.AsyncState;
        if (socket == null || !socket.Connected)
            return;
        int count = socket.EndSend(ar);

        ByteArray ba;
        lock (writeQueue)
        {
            ba = writeQueue.First();
        }

        ba.readIndex += count;
        //如果这个byteArray已经发送完成
        if (ba.Length == 0)
        {
            lock (writeQueue)
            {
                //清除
                writeQueue.Dequeue();
                //取到下一个
                ba = writeQueue.First();
            }
        }

        //没有发送完成，还有消息要继续发送
        if (ba != null)
        {
            socket.BeginSend(ba.bytes, ba.readIndex, ba.Length, 0, SendCallback, socket);
        }

        if (isClosing)
        {
            socket.Close();
        }
    }

    /// <summary>
    /// 处理消息
    /// </summary>
    private static void MsgUpdate()
    {
        //没有消息
        if (msgList.Count == 0)
            return;
        for (int i = 0; i < processMsgCount; i++)
        {
            IExtensible msgBase = null;
            lock (msgList)
            {
                if (msgList.Count > 0)
                {
                    msgBase = msgList[0];
                    msgList.RemoveAt(0);
                }
            }
            if (msgBase != null)
            {
                PropertyInfo info = msgBase.GetType().GetProperty("protoName");
                string protoName = info.GetValue(msgBase).ToString();
                FireMsg(protoName, msgBase);
                Debug.Log(protoName);
            }
            else
            {
                break;
            }
        }
    }

    private static void PingUpdate()
    {
        if (!isUsePing)
            return;

        if (Time.time - lastPingTime > pingInterval)
        {
            //发送
            MsgPing msg = new MsgPing();
            Send(msg);
            lastPingTime = Time.time;
        }

        //断开的处理
        if (Time.time - lastPongTime > pingInterval * 4)
        {
            Close();
        }
    }

    public static void Update()
    {
        PingUpdate();
        MsgUpdate();
    }

    private static void OnMsgPong(IExtensible msgBase)
    {
        lastPongTime = Time.time;
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