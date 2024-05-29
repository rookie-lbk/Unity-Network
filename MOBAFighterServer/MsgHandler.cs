using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class MsgHandler
{
    public static void MsgPing(ClientState s, MsgBase msgBase)
    {
        Console.WriteLine("MsgPing:" + s.socket.RemoteEndPoint);
        s.lastPingTime = NetManager.GetTimeStamp();
        MsgPong msgPong = new MsgPong();
        NetManager.Send(s, msgPong);
    }

}