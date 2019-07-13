using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BaiduSpeechDemo
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private Speakers speakers;

        private MediaPlayer mediaPlayer = new MediaPlayer();

        private System.Timers.Timer initTimer;

        private string file;

        public MainWindow()
        {
            InitializeComponent();

            speakers = Speakers.Instance();

            initTimer = new System.Timers.Timer(1000);
            initTimer.Elapsed += OnTimedEvent;
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            if (initTimer.Enabled)
                initTimer.Stop();

            this.Dispatcher.Invoke(() =>
            {
                if(speakers.initStatus)
                {
                    TipsText.Text = "初始化 SDK 成功，请输入文本测试！";
                    InitButton.IsEnabled = false;
                    APIKey.IsEnabled = false;
                    SecretKey.IsEnabled = false;
                    SpeakText.IsEnabled = true;
                    PlaySoundButton.IsEnabled = true;
                }
                else
                {
                    TipsText.Text = "初始化 SDK 失败，请尝试重新初始化！";
                }
            });
        }

        private void PlaySound_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if(speakers.initStatus)
                {
                    if (!string.IsNullOrEmpty(SpeakText?.Text))
                    {
                        file = speakers?.Speak(SpeakText.Text);
                        if (!string.IsNullOrEmpty(file))
                        {
                            TipsText.Text = string.Format("语音文件保存在：{0}", file);
                            OpenExplorerButton.Visibility = Visibility.Visible;
                            mediaPlayer.Open(new Uri(file, UriKind.Relative));
                            mediaPlayer.Play();
                            return;
                        }
                        else
                        {
                            TipsText.Text = "语音合成失败！";
                        }
                    }
                    else
                    {
                        TipsText.Text = "文本为空！";
                    }
                }
                else
                {
                    TipsText.Text = "请先初始化 SDK 再进行语音合成！";
                }
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("PlaySound_Click " + e1.Message.ToString());
            }
            OpenExplorerButton.Visibility = Visibility.Hidden;
            file = "";
        }

        private void OpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(file))
                {
                    System.Diagnostics.Process.Start("Explorer.exe", System.IO.Path.GetDirectoryName(file));
                }
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("OpenExplorer_Click " + e1.Message.ToString());
            }
        }

        private void Init_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if(!speakers.initStatus)
                {
                    if (!string.IsNullOrEmpty(APIKey.Text) && !string.IsNullOrEmpty(SecretKey.Text))
                    {
                        TipsText.Text = "正在初始化......";
                        speakers.Init(APIKey.Text, SecretKey.Text);

                        if (initTimer.Enabled)
                            initTimer.Stop();
                        initTimer.Start();
                    }
                    else
                    {
                        TipsText.Text = "请输入正确的 API Key 与 Secret Key！";
                    }
                }
                else
                {
                    TipsText.Text = "初始化SDK成功，请输入文本测试！";
                }
            }
            catch (Exception e1)
            {
                System.Diagnostics.Debug.WriteLine("OpenExplorer_Click " + e1.Message.ToString());
            }
        }
    }
}
