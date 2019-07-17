using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BaiduSpeechDemo
{
    public class Speakers
    {
        // 单例
        private static Speakers _instance = null;

        public static Speakers Instance()
        {
            return _instance ?? (_instance = new Speakers());
        }

        private struct BaiduTTSConfig
        {
            // API Key
            public string api_key;
            // Secret Key
            public string secret_key;
            // （必填）固定值zh。语言选择,目前只有中英文混合模式，填写固定值zh
            public string lan;
            // （必填）客户端类型选择，web端填写固定值1
            public string ctp;
            // （必填）用户唯一标识，用来计算UV值。建议填写能区分用户的机器 MAC 地址或 IMEI 码，长度为60字符以内
            public string cuid;
            // （必填）开放平台获取到的开发者 access_token
            public string tok;
            // （必填）合成的文本，使用UTF-8编码。小于2048个中文字或者英文数字。（文本在百度服务器内转换为GBK后，长度必须小于4096字节）
            public string tex;
            // （选填）音量，取值0-15，默认为5中音量
            public string vol;
            // （选填）发音人选择, 0为普通女声，1为普通男生，3为情感合成-度逍遥，4为情感合成-度丫丫，默认为普通女声
            public string per;
            // （选填）语速，取值0-15，默认为5中语速
            public string spd;
            // （选填）音调，取值0-15，默认为5中语调
            public string pit;
            // （选填）3为mp3格式(默认)； 4为pcm-16k；5为pcm-8k；6为wav（内容同pcm-16k）; 注意aue=4或者6是语音识别要求的格式，但是音频内容不是语音识别要求的自然人发音，所以识别效果会受影响。
            public string aue;
        }
        private BaiduTTSConfig baiduTTSConfig;

        public bool isInit = false;

        // 文本请求队列
        private Queue<string> textRequestQueue = new Queue<string>();
        private Thread textRequestQueueThread;

        // 流播放队列
        private Queue<Stream> streamPlayQueue = new Queue<Stream>();
        private static readonly int streamPlayQueueMaxCount = 10;
        private Thread streamPlayQueueThread;

        private Speakers()
        {
            try
            {
                // 使用默认参数构造百度TTS
                baiduTTSConfig = new BaiduTTSConfig()
                {
                    lan = "zh",
                    ctp = "1",
                    cuid = GetMacAddress(),
                    vol = "15",
                    per = "0",
                    spd = "5",
                    pit = "5",
                    aue = "6"
                };
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("Speakers Speakers, " + e1.Message);
            }
        }

        public void Init(string api_key, string secret_key)
        {
            try
            {
                if(!isInit && !string.IsNullOrEmpty(api_key) && !string.IsNullOrEmpty(secret_key))
                {
                    baiduTTSConfig.api_key = api_key;
                    baiduTTSConfig.secret_key = secret_key;

                    // 初始化百度 TTS，获得 token
                    ThreadPool.QueueUserWorkItem(new WaitCallback((state) =>
                    {
                        try
                        {
                            string url = string.Format("https://openapi.baidu.com/oauth/2.0/token?grant_type=client_credentials&client_id={0}&client_secret={1}",
                                    baiduTTSConfig.api_key, baiduTTSConfig.secret_key);

                            string json = HttpPostRequestGetJson(url);

                            // 检验有效期，单位秒
                            string expires_in = GetArribute(json, "expires_in");
                            if (long.Parse(expires_in) <= 0)
                            {
                                // 语音合成已经过期
                                System.Diagnostics.Debug.WriteLine(string.Format("Baidu TTS has expired, request link is: {0}；return Json is: {1}", url, json));
                                isInit = false;
                                return;
                            }

                            // 检验 token
                            string token = GetArribute(json, "access_token");
                            baiduTTSConfig.tok = token ?? baiduTTSConfig.tok;

                            isInit = true;
                        }
                        catch (Exception e1)
                        {
                            System.Diagnostics.Debug.WriteLine("Speakers Init QueueUserWorkItem, " + e1.Message);
                            isInit = false;
                        }
                    }), null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Speakers Init," + ex.Message);
            }
        }

        /// <summary>
        /// TTS 语音合成
        /// </summary>
        /// <param name="text">需要语音合成的文本内容</param>
        /// 
        ///        Speak
        ///      |  文本  |
        ///      |  文本  |    文本请求队列（textRequestQueue）
        ///      |  文本  |
        ///  HttpPostRequestGetStream
        ///      |   流   |    
        ///      |   流   |    流播放队列（streamPlayQueue）
        ///      |   流   |
        ///      SoundPlayer
        ///   
        public void Speak(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    if (!isInit || string.IsNullOrEmpty(text))
                        return;

                    // 文本请求入队
                    lock (textRequestQueue)
                    {
                        textRequestQueue.Enqueue(text);
                    }

                    if (streamPlayQueue.Count() >= streamPlayQueueMaxCount)
                        return;

                    // 线程为空或者线程退出（正常终止，异常退出）时重新构造和启动线程
                    if (textRequestQueueThread == null || textRequestQueueThread.ThreadState == ThreadState.Stopped)
                    {
                        textRequestQueueThread = new Thread(TextRequestQueueConsumer);
                        textRequestQueueThread.Start();
                    }
                }
                catch (Exception e1)
                {
                    System.Diagnostics.Debug.WriteLine("Speakers Speak, " + e1.Message);
                }
            }
        }

        // 文本请求队列消费者
        // 文本从文本请求队列出队，用于 Http 请求，把请求到的音频文件流入队流播放队列
        private void TextRequestQueueConsumer()
        {
            try
            {
                while (textRequestQueue.Count() > 0 && streamPlayQueue.Count() < streamPlayQueueMaxCount)
                {
                    string text;
                    // 文本请求出队
                    lock (textRequestQueue)
                    {
                        text = textRequestQueue.Dequeue();
                    }
                    string url = String.Format("http://tsn.baidu.com/text2audio?lan={0}&ctp={1}&cuid={2}&tok={3}&tex={4}&vol={5}&per={6}&spd={7}&pit={8}&aue={9}",
                        baiduTTSConfig.lan, baiduTTSConfig.ctp, baiduTTSConfig.cuid, baiduTTSConfig.tok, text, baiduTTSConfig.vol, baiduTTSConfig.per, baiduTTSConfig.spd, baiduTTSConfig.pit, baiduTTSConfig.aue);

                    // Http 请求音频流
                    Stream fileStream = HttpPostRequestGetStream(url);
                    if (fileStream != null)
                    {
                        // 音频流入队
                        lock (streamPlayQueue)
                        {
                            streamPlayQueue.Enqueue(fileStream);
                        }

                        // 线程为空或者线程退出（正常终止，异常退出）时重新构造和启动线程
                        if (streamPlayQueueThread == null || streamPlayQueueThread.ThreadState == ThreadState.Stopped)
                        {
                            streamPlayQueueThread = new Thread(StreamPlayQueueConsumer);
                            streamPlayQueueThread.Start();
                        }
                    }
                }
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("Speakers TextRequestQueueConsumer, " + e1.Message);
            }
        }

        // 流播放队列消费者
        // 流从流播放队列出队，PlaySync 同步播放
        private void StreamPlayQueueConsumer()
        {
            try
            {
                while (streamPlayQueue.Count() > 0)
                {
                    // 音频流出队
                    lock (streamPlayQueue)
                    {
                        using (System.Media.SoundPlayer soundPlayer = new System.Media.SoundPlayer(streamPlayQueue.Dequeue()))
                        {
                            // 同步播放
                            soundPlayer.PlaySync();
                        }
                    }
                }
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("Speakers StreamPlayQueueConsumer, " + e1.Message);
            }
        }

        // Http 请求 Json 数据
        public static string HttpPostRequestGetJson(string url)
        {
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            try
            {
                request = (HttpWebRequest)HttpWebRequest.Create(url);
                request.Timeout = 5000;
                request.Method = "POST";
                response = (HttpWebResponse)request.GetResponse();
                StreamReader sr = new StreamReader(response.GetResponseStream());
                string jsonstr = sr.ReadToEnd();
                return jsonstr;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Speakers HttpPostRequestGetJson," + ex.Message);
                return string.Empty;
            }
            finally
            {
                response.Close();
            }
        }

        // 使用 Media.SoundPlayer 播放 Http 请求的 wav 文件流
        // 使用同步播放，播放完后才返回
        public static Stream HttpPostRequestGetStream(string url)
        {
            try
            {
                if (!string.IsNullOrEmpty(url))
                {
                    System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(url);
                    req.Timeout = 5000;
                    req.Method = "POST";
                    System.Net.HttpWebResponse rsp = (System.Net.HttpWebResponse)req.GetResponse();

                    if (rsp?.ContentLength > 0)
                    {
                        return rsp.GetResponseStream();
                    }
                }
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("Speakers HttpPostRequestGetStream," + e1.Message.ToString());
            }
            return null;
        }

        // Json 解析
        public static string GetArribute(string json, string key)
        {
            try
            {
                key = string.Format("\"{0}\":", key);
                int pos = json.IndexOf(key);
                if (pos < 0)
                    return "";
                json = json.Substring(pos + key.Length);
                json = json.Replace("\"", "");
                pos = json.IndexOf(",");
                if (pos < 0)
                {
                    pos = json.IndexOf("}");
                }
                if (pos < 0)
                    return "";
                string result = json.Substring(0, pos);
                return result;
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("Speakers GetArribute," + e1.Message.ToString());
            }
            return "";
        }

        // 获取本机 MAC 地址
        public static string GetMacAddress()
        {
            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface ni in interfaces)
                {
                    return BitConverter.ToString(ni.GetPhysicalAddress().GetAddressBytes());
                }
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("Speakers GetMacAddress," + e1.Message.ToString());
            }
            return "00-00-00-00-00-00";
        }
    }
}
