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
        private BaiduTTSConfig baiduTTS;

        public bool initStatus;

        public Speakers()
        {
            try
            {
                // 使用默认参数构造百度TTS
                baiduTTS = new BaiduTTSConfig()
                {
                    lan = "zh",
                    ctp = "1",
                    cuid = GetMacByNetworkInterface(),
                    vol = "9",
                    per = "0",
                    spd = "5",
                    pit = "5",
                    aue = "3"
                };
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("Speakers Speakers, " + e1.Message);
            }
            initStatus = false;
        }

        public void Init(string api_key, string secret_key)
        {
            try
            {
                if(!string.IsNullOrEmpty(api_key) && !string.IsNullOrEmpty(secret_key))
                {
                    baiduTTS.api_key = api_key;
                    baiduTTS.secret_key = secret_key;

                    // 初始化百度 TTS，获得 token
                    ThreadPool.QueueUserWorkItem(new WaitCallback((state) =>
                    {
                        try
                        {
                            string url = string.Format("https://openapi.baidu.com/oauth/2.0/token?grant_type=client_credentials&client_id={0}&client_secret={1}",
                                    baiduTTS.api_key, baiduTTS.secret_key);

                            string json = HttpPostRequest(url);

                            // 检验有效期，单位秒
                            string expires_in = GetArribute(json, "expires_in");
                            if (long.Parse(expires_in) <= 0)
                            {
                                // 语音合成已经过期
                                System.Diagnostics.Debug.WriteLine(string.Format("Baidu TTS has expired, request link is: {0}；return Json is: {1}", url, json));
                                initStatus = false;
                                return;
                            }

                            // 检验 token
                            string token = GetArribute(json, "access_token");
                            baiduTTS.tok = token ?? baiduTTS.tok;

                            initStatus = true;
                        }
                        catch (Exception e1)
                        {
                            System.Diagnostics.Debug.WriteLine("Speakers Init QueueUserWorkItem, " + e1.Message);
                            initStatus = false;
                        }
                    }), null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Speakers Init," + ex.Message);
                initStatus = false;
            }
        }

        /// <summary>
        /// 语音合成
        /// </summary>
        /// <param name="text">需要语音合成的文本内容</param>
        /// <returns>网络请求回来的音频文件在本地保存的路径</returns>
        public string Speak(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    baiduTTS.tex = text;
                    string url = String.Format("http://tsn.baidu.com/text2audio?lan={0}&ctp={1}&cuid={2}&tok={3}&tex={4}&vol={5}&per={6}&spd={7}&pit={8}&aue={9}",
                        baiduTTS.lan, baiduTTS.ctp, baiduTTS.cuid, baiduTTS.tok, baiduTTS.tex, baiduTTS.vol, baiduTTS.per, baiduTTS.spd, baiduTTS.pit, baiduTTS.aue);
                    string mp3FilePathName = String.Format(@"{0}\{1}_{2}.mp3", GetCachePath(), DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss"), baiduTTS.tex);
                    
                    return HttpPostMessage(url, mp3FilePathName);
                }
                catch (Exception e1)
                {
                    System.Diagnostics.Debug.WriteLine("Speakers Speak, " + e1.Message);
                }
            }
            return "";
        }
        
        // Http 请求 Json 数据
        private string HttpPostRequest(string url)
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
                System.Diagnostics.Debug.WriteLine("Speakers HttpPostRequest," + ex.Message);
                return string.Empty;
            }
            finally
            {
                response.Close();
            }
        }

        // Http post 消息来下载语音文件
        private string HttpPostMessage(string url, string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(filePath))
                {
                    return "";
                }

                System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(url);
                req.Timeout = 3000;
                System.Net.HttpWebResponse rsp = (System.Net.HttpWebResponse)req.GetResponse();

                if (rsp?.ContentLength > 0)
                {
                    System.IO.Stream netStream = rsp.GetResponseStream();
                    System.IO.Stream fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create);
                    byte[] buffer = new byte[8 * 1024];
                    int readSize = netStream.Read(buffer, 0, (int)buffer.Length);

                    while (readSize > 0)
                    {
                        fileStream.Write(buffer, 0, readSize);
                        readSize = netStream.Read(buffer, 0, (int)buffer.Length);
                    }
                    netStream.Close();
                    fileStream.Close();
                    return filePath;
                }
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("Speakers HttpPostMessage," + e1.Message.ToString());
            }
            return "";
        }

        // 获取缓存目录
        private string GetCachePath()
        {
            try
            {
                string TempDir = System.IO.Path.GetTempPath();
                if (TempDir == null || TempDir.Length == 0)
                {
                    return "";
                }
                string CachePath = System.IO.Path.Combine(TempDir, @"BaiduSpeechDemo\Cache");
                if (System.IO.Directory.Exists(CachePath) == false)
                {
                    System.IO.Directory.CreateDirectory(CachePath);
                }
                return CachePath;
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("Speakers GetCachePath," + e1.Message.ToString());
            }
            return "";
        }

        // 获取本机 MAC 地址
        public static string GetMacByNetworkInterface()
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
                System.Diagnostics.Debug.WriteLine("Speakers GetMacByNetworkInterface," + e1.Message.ToString());
            }
            return "00-00-00-00-00-00";
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
    }
}
