﻿using AngleSharp.Dom;
using Core.LogModule;
using Core.Network;
using Core.Network.Methods;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Core.Network.Methods.Room;

namespace Core.LiveChat
{
    public class LiveChatListener
    {
        #region Properties
        private ClientWebSocket m_client;
        private bool _disposed = false;
        private byte[] m_ReceiveBuffer;
        private CancellationTokenSource m_innerRts;
        private long RoomId = 0;
        private DanMuWssInfo WssInfo = new();

        public event EventHandler<MessageEventArgs> MessageReceived;
        public event EventHandler<EventArgs> DisposeSent;
        public bool State = false;

        #endregion

        #region Public Method
        public LiveChatListener(long roomId)
        {
            RoomId = roomId;
        }

        public async void Connect()
        {
            try
            {
                m_ReceiveBuffer = new byte[8192 * 1024];
                State = true;
                await ConnectAsync();
            }
            catch (Exception e)
            {
                Log.Error(nameof(LiveChatListener) + "_" + nameof(Connect), $"LiveChatListener初始化Connect出现错误", e, true);
                Dispose();
            }
        }

        public void Close()
        {
            m_ReceiveBuffer = null;

            try
            {
                if (m_innerRts != null)
                {
                    m_innerRts.Cancel();
                }
            }
            catch (Exception)
            { }
            try
            {
                m_client.Dispose();
            }
            catch (Exception) { }
            try
            {
                m_innerRts.Dispose();
            }
            catch (Exception) { }
            try
            {
                m_ReceiveBuffer = null;
            }
            catch (Exception) { }
        }
        public void Dispose()
        {
            if (!_disposed)
            {
                Close();
            }
            if (DisposeSent != null)
                DisposeSent.Invoke(this, EventArgs.Empty);
            _disposed = true;
        }



        #endregion

        #region Private Method

        private DanMuWssInfo GetWssInfo()
        {
            string WebText = Get.GetBody($"{Config.Core._LiveDomainName}/xlive/web-room/v1/index/getDanmuInfo?id=" + RoomId, true);
            DanMuWssInfo roomInfo = new();
            try
            {
                roomInfo = JsonSerializer.Deserialize<DanMuWssInfo>(WebText);
                return roomInfo;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task ConnectAsync()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("");
            }
            m_client = new ClientWebSocket();
            m_innerRts = new CancellationTokenSource();
            JObject JO = new JObject();
            try
            {
                WssInfo = GetWssInfo();
                string URL = "wss://" + WssInfo.data.host_list[new Random().Next(0, WssInfo.data.host_list.Count)].host + "/sub";
                //Log.Info(nameof(LiveChatListener) + "_" + nameof(ConnectAsync), $"弹幕连接地址:[{URL}]");
                await m_client.ConnectAsync(new Uri(URL), new CancellationTokenSource().Token);
            }
            catch (Exception e)
            {
                Log.Error(nameof(LiveChatListener) + "_" + nameof(ConnectAsync), $"LiveChatListener连接发生错误", e, true);
                Dispose();
            }
            await _sendObject(7, new
            {
                uid = long.Parse(RuntimeObject.Account.AccountInformation.Uid),
                roomid = RoomId,
                protover = 3,
                buvid = RuntimeObject.Account.AccountInformation.Buvid,
                platform = "web",
                type = 2,
                key = WssInfo.data.token
            });

            _ = _innerLoop().ContinueWith((t) =>
           {
               if (t.IsFaulted)
               {
                   if (!m_innerRts.IsCancellationRequested)
                   {
                       MessageReceived(this, new ExceptionEventArgs(t.Exception.InnerException));
                       m_innerRts.Cancel();
                   }
               }
               else
               {
                   Log.Info(nameof(LiveChatListener) + "_" + nameof(ConnectAsync), $"LiveChatListener连接断开");
               }
               try
               {
                   m_client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).Wait();
               }
               catch (Exception ex)
               {
                   Log.Error(nameof(LiveChatListener) + "_" + nameof(ConnectAsync), $"LiveChatListener连接发生意料外的错误", ex, true);
                   Dispose();
               }
           });
            _ = _innerHeartbeat();
        }

        private async Task _innerHeartbeat()
        {
             //Log.Info(nameof(_innerHeartbeat), $"_innerHeartbeat:start");
            while (!m_innerRts.IsCancellationRequested)
            {
                //Log.Info(nameof(_innerHeartbeat), $"_innerHeartbeat:in");
                try
                {
                    //UnityEngine.Debug.Log("heartbeat");
                    await _sendBinary(2, Encoding.UTF8.GetBytes("[object Object]"));
                    await Task.Delay(10 * 1000, m_innerRts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    string A = e.ToString();
                }
                //Log.Info(nameof(_innerHeartbeat), $"_innerHeartbeat:over");
            }
        }

        private async Task _innerLoop()
        {
#if DEBUG
            //InfoLog.InfoPrintf("LiveChatListener开始连接，房间号:" + TroomId, InfoLog.InfoClass.Debug);
            Console.WriteLine($"直播间长连握手开始(room_id:{RoomId})");
#endif
            while (!m_innerRts.IsCancellationRequested)
            {
                try
                {
                    WebSocketReceiveResult result;
                    int length = 0;
                    do
                    {
                        try
                        {
                            result = await m_client.ReceiveAsync(
                          new ArraySegment<byte>(m_ReceiveBuffer, length, m_ReceiveBuffer.Length - length),
                          m_innerRts.Token);
                            length += result.Count;
                        }
                        catch (Exception e)
                        {
                            throw;
                        }
                    }
                    while (!result.EndOfMessage);
                    DepackDanmakuData(m_ReceiveBuffer);
                }
                catch (OperationCanceledException ex)
                {
                    Log.Info(nameof(_innerLoop) + "_OperationCanceledException", $"_sendObject:{ex.ToString()}");
                    continue;
                }
                catch (ObjectDisposedException ex)
                {
                     Log.Info(nameof(_innerLoop) + "_OperationCanceledException", $"_sendObject:{ex.ToString()}");
                    continue;
                }
                catch (WebSocketException we)
                {
                     Log.Info(nameof(_innerLoop) + "_OperationCanceledException", $"_sendObject:{we.ToString()}");
                    throw we;
                }
                catch (Newtonsoft.Json.JsonException ex)
                {
                     Log.Info(nameof(_innerLoop) + "_OperationCanceledException", $"_sendObject:{ex.ToString()}");
                    continue;
                }
                catch (Exception e)
                {
                     Log.Info(nameof(_innerLoop) + "_OperationCanceledException", $"_sendObject:{e.ToString()}");
                    //UnityEngine.Debug.LogException(e);
                    throw e;
                }
            }
        }

        /// <summary>
        /// 消息拆包
        /// </summary>
        private void DepackDanmakuData(byte[] messages)
        {
            byte[] headerBuffer = new byte[16];
            Array.Copy(messages, 0, headerBuffer, 0, 16);
            DanmakuProtocol protocol = new DanmakuProtocol(headerBuffer);


            if (protocol.PacketLength < 16)
            {
                Log.Warn(nameof(LiveChatListener) + "_" + nameof(DepackDanmakuData), $"LiveChatListener初始化bodyLength出现错误长度<16");
                return;
            }
            int bodyLength = protocol.PacketLength - 16;
            if (bodyLength == 0)
            {
                //continue;
                Log.Warn(nameof(LiveChatListener) + "_" + nameof(DepackDanmakuData), $"LiveChatListener初始化bodyLength出现错误长度0");
                return;
            }
            byte[] buffer = new byte[bodyLength];
            Array.Copy(messages, 16, buffer, 0, bodyLength);
            switch (protocol.Version)
            {
                case 1:
                    ProcessDanmakuData(protocol.Operation, buffer, bodyLength);
                    break;
                case 2:
                    {
                        var ms = new MemoryStream(buffer, 2, bodyLength - 2);
                        var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                        while (deflate.Read(headerBuffer, 0, 16) > 0)
                        {
                            protocol = new DanmakuProtocol(headerBuffer);
                            bodyLength = protocol.PacketLength - 16;
                            if (bodyLength == 0)
                            {
                                continue; // 没有内容了
                            }
                            if (buffer.Length < bodyLength) // 不够长再申请
                            {
                                buffer = new byte[bodyLength];
                            }
                            deflate.Read(buffer, 0, bodyLength);
                            ProcessDanmakuData(protocol.Operation, buffer, bodyLength);
                        }
                        ms.Dispose();
                        deflate.Dispose();
                        break;
                    }
                case 3:
                    using (var inputStream = new MemoryStream(buffer))
                    using (var outputStream = new MemoryStream())
                    using (var decompressionStream = new BrotliStream(inputStream, CompressionMode.Decompress))
                    {
                        decompressionStream.CopyTo(outputStream);
                        buffer = outputStream.ToArray();
                    }
                    ProcessDanmakuData(protocol.Operation, buffer, buffer.Length, true);
                    break;

                default:

                    break;
            }
        }

        /// <summary>
        /// 消息处理
        /// </summary>
        private void ProcessDanmakuData(int opt, byte[] buffer, int length, bool IsBrotli = false)
        {
            switch (opt)
            {
                case 3:
                    {
                        if (length == 4)
                        {
                            int 人气值 = buffer[3] + buffer[2] * 255 + buffer[1] * 255 * 255 + buffer[0] * 255 * 255 * 255;
                            _parse("{\"cmd\":\"LiveP\",\"LiveP\":" + 人气值 + ",\"roomID\":" + RoomId + "}");
                        }
                        break;
                    }
                case 5:
                    {
                        try
                        {
                            if (IsBrotli)
                            {
                                do
                                {
                                    int len = buffer[3] + (buffer[2] * 256) + (buffer[1] * 256 * 256) + (buffer[0] * 256 * 256 * 256);
                                    byte[] a = new byte[len - 16];
                                    Array.Copy(buffer, 16, a, 0, len - 16);
                                    string jsonBody = Encoding.UTF8.GetString(a, 0, len - 16);
                                    jsonBody = Regex.Unescape(jsonBody);
                                    _parse(jsonBody);
                                    byte[] b = new byte[buffer.Length - len];
                                    Array.Copy(buffer, len, b, 0, buffer.Length - len);
                                    buffer = b;
                                } while (buffer.Length > 0);
                            }
                            else
                            {
                                string jsonBody = Encoding.UTF8.GetString(buffer, 0, length);
                                jsonBody = Regex.Unescape(jsonBody);
                                _parse(jsonBody);
                            }

                        }
                        catch (Exception ex)
                        {
                            if (ex is Newtonsoft.Json.JsonException || ex is KeyNotFoundException)
                            {
                                //LogEvent?.Invoke(this, new LogEventArgs { Log = $@"[{_roomId}] 弹幕识别错误 {json}" });
                            }
                            else
                            {
                                //LogEvent?.Invoke(this, new LogEventArgs { Log = $@"[{_roomId}] {ex}" });
                            }
                        }
                        break;
                    }
                default:
                    break;
            }
        }

        private void _parse(string jsonBody)
        {
            var obj = new JsonObject();
            try
            {
                jsonBody = ReplaceString(jsonBody);
                if (jsonBody.Contains("DANMU_MSG"))
                {
                    jsonBody = jsonBody.Replace("\"extra\":\"", "\"extra\":");
                    jsonBody = jsonBody.Replace("\"{}\",", "");
                    jsonBody = jsonBody.Replace("}\",\"", "},\"");
                }


                obj = JsonNode.Parse(jsonBody).AsObject();
            }
            catch (Exception) { return; }
            string cmd = (string)obj["cmd"];
            switch (cmd)
            {

                //弹幕信息
                case "DANMU_MSG":
                    MessageReceived(this, new DanmuMessageEventArgs(obj));
                    break;
                //SC信息
                case "SUPER_CHAT_MESSAGE":
                    MessageReceived(this, new SuperchatEventArg(obj));
                    break;
                //礼物
                case "SEND_GIFT":
                    MessageReceived(this, new SendGiftEventArgs(obj));
                    break;
                //舰组信息(上舰)
                case "GUARD_BUY":
                    MessageReceived(this, new GuardBuyEventArgs(obj));
                    break;
                //小时榜单变动通知
                case "ACTIVITY_BANNER_UPDATE_V2":
                    break;
                //礼物combo
                case "COMBO_SEND":
                    break;
                //进场特效
                case "ENTRY_EFFECT":
                    break;
                //续费舰长
                case "USER_TOAST_MSG":
                    break;
                //在房间内续费了舰长
                case "NOTICE_MSG":
                    break;
                //欢迎
                case "WELCOME":
                    break;
                //人气值(心跳数据)
                case "LiveP":
                    break;
                //管理员警告
                case "WARNING":
                    MessageReceived(this, new WarningEventArg(obj));
                    break;
                //开播_心跳
                case "LIVE":
                    //应该还有收费直播的鉴权信息，但是这里就不细说了
                    break;
                //下播_心跳
                case "PREPARING":
                    break;
                case "INTERACT_WORD":
                    //进场消息（弹幕区展示进场消息，粉丝勋章，姥爷，榜单）和用户关注、分享、特别关注直播间
                    break;
                case "PANEL":
                    //小时榜信息更新
                    break;
                case "ONLINE_RANK_COUNT":
                    //服务等级（降级后会变化）
                    break;
                case "ONLINE_RANK_V2":
                    //高能榜更新
                    break;
                case "ROOM_BANNER":
                    //房间横幅信息，应该就是置顶的那个跳转广告
                    break;
                case "ACTIVITY_RED_PACKET":
                    //红包抽奖弹幕
                    break;
                //切断直播间
                case "CUT_OFF":
                    MessageReceived(this, new CutOffEventArg(obj));
                    break;
                default:
                    //Console.WriteLine(cmd);
                    //Log.Log.AddLog(nameof(LiveChatListener), Log.LogClass.LogType.Info, $"收到未知CMD:{cmd}");
                    MessageReceived(this, new MessageEventArgs(obj));
                    break;
            }
            return;
        }

        /// <summary>
        ///   替换部分字符串
        /// </summary>
        /// <param name="sPassed">需要替换的字符串</param>
        /// <returns></returns>
        private static string ReplaceString(string JsonString)
        {
            if (JsonString == null) { return JsonString; }
            if (JsonString.Contains("\\"))
            {
                JsonString = JsonString.Replace("\\", "\\\\");
            }
            JsonString = Regex.Replace(JsonString, @"[\n\r]", "");
            JsonString = JsonString.Trim();
            return JsonString;
        }

        private async Task _sendObject(int type, object obj)
        {

            //string jsonBody = JsonConvert.SerializeObject(obj, Formatting.None);
            string jsonBody = JsonSerializer.Serialize(obj);
             //Log.Info(nameof(LiveChatListener) + "_" + nameof(_sendObject), $"_sendObject:{jsonBody}");
            //Log.Log.AddLog(nameof(LiveChatListener), Log.LogClass.LogType.Info, $"发送WS信息:\r\n{jsonBody}");
            await _sendBinary(type, System.Text.Encoding.UTF8.GetBytes(jsonBody));
        }
        private async Task _sendBinary(int type, byte[] body)
        {
            byte[] head = new byte[16];
            using (MemoryStream ms = new MemoryStream(head))
            {
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.WriteBE(16 + body.Length);
                    bw.WriteBE((ushort)16);
                    bw.WriteBE((ushort)1);
                    bw.WriteBE(type);
                    bw.WriteBE(1);
                }
            }
            byte[] tail = new byte[16 + body.Length];
            Array.Copy(head, 0, tail, 0, 16);
            Array.Copy(body, 0, tail, 16, body.Length);
            await m_client.SendAsync(new ArraySegment<byte>(tail), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        #endregion

        #region private Class

        /// <summary>
        /// 消息协议
        /// </summary>
        private class DanmakuProtocol
        {
            /// <summary>
            /// 消息总长度 (协议头 + 数据长度)
            /// </summary>
            public int PacketLength;
            /// <summary>
            /// 消息头长度 (固定为16[sizeof(DanmakuProtocol)])
            /// </summary>
            public short HeaderLength;
            /// <summary>
            /// 消息版本号
            /// </summary>
            public short Version;
            /// <summary>
            /// 消息类型
            /// </summary>
            public int Operation;
            /// <summary>
            /// 参数, 固定为1
            /// </summary>
            public int Parameter;

            /// <summary>
            /// 转为本机字节序
            /// </summary>
            public DanmakuProtocol(byte[] buff)
            {
                PacketLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buff, 0));
                HeaderLength = IPAddress.HostToNetworkOrder(BitConverter.ToInt16(buff, 4));
                Version = IPAddress.HostToNetworkOrder(BitConverter.ToInt16(buff, 6));
                Operation = IPAddress.HostToNetworkOrder(BitConverter.ToInt32(buff, 8));
                Parameter = IPAddress.HostToNetworkOrder(BitConverter.ToInt32(buff, 12));
            }
        }

        private class DanMuWssInfo
        {
            public long code { get; set; }
            public string message { get; set; }
            public long ttl { get; set; }
            public Data data { get; set; } = new();
            public class Data
            {
                public long uid { set; get; }
                public string token { set; get; }
                public List<Host> host_list { set; get; } = new List<Host>();
                public class Host
                {
                    public string host { set; get; }
                    public int port { set; get; }
                    public int wss_port { set; get; }
                    public int ws_port { set; get; }
                }
            }
        }


        #endregion
    }
}
