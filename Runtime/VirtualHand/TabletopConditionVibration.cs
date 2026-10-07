using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AvatarExperiments.VirtualHand
{
    [DisallowMultipleComponent]
    public sealed class TabletopConditionVibration : MonoBehaviour
    {
        public TabletopExperiment experiment;
        public string portName="COM4",channelList="0,1,2,3";
        public string Status{get;private set;}="ESP32未接続";
        public bool Connected=>transport!=null&&transport.Connected;
        public bool Connecting=>transport is AsyncHapticTransport worker&&worker.Connecting;
        public bool Active{get;private set;}
        public bool SessionActive{get;private set;}
        public event Action<bool,double> OutputChanged;
        [NonSerialized] public Func<ITabletopHapticTransport> TransportFactory;
        ITabletopHapticTransport transport;
        TrialSettings trial;
        int[] channels;
        string connectedPort,connectedChannelList;
        bool connectionReported,faulting,failed;
        double elapsed;
        const string Header="trial,condition,event,trial_s,realtime_s,channels,amplitude,frequency_hz,delay_s,delivery";
        public void Connect()
        {
            if(experiment&&experiment.Running){Status="試行中は接続を変更できません。";return;}
            Disconnect();failed=false;
            try
            {
                channels=channelList.Split(',').Select(p=>int.Parse(p.Trim(),CultureInfo.InvariantCulture)).ToArray();
                if(channels.Length==0||channels.Any(c=>c<0||c>15)||channels.Distinct().Count()!=channels.Length)
                    throw new ArgumentException("チャンネルは重複しない0〜15を指定してください。");
                connectedPort=portName;connectedChannelList=channelList;connectionReported=false;
                if(TransportFactory!=null)transport=TransportFactory();
                else
                {
                    if(Application.platform!=RuntimePlatform.WindowsEditor&&Application.platform!=RuntimePlatform.WindowsPlayer)
                        throw new PlatformNotSupportedException("ESP32 USB出力はWindows PCで使用します。");
                    if(string.IsNullOrWhiteSpace(portName))throw new ArgumentException("COMポートを指定してください。");
                    string port=portName;
                    if(!Esp32HapticTransport.Available)throw new InvalidOperationException("Install the ESP32HapticController Unity Runtime folder first (see VirtualHand.md).");
                    string path=Path.Combine(Application.persistentDataPath,"VirtualHandLogs",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-vibration.csv");
                    transport=new AsyncHapticTransport(()=>new Esp32HapticTransport(port),path,Header);
                }
                foreach(int channel in channels)transport.Prepare(channel);
                Status=connectedPort+" CH "+connectedChannelList+" 接続準備中（出力停止）";
            }
            catch(Exception e){Fault(e);}
        }
        public bool CanBegin(TrialSettings settings)
        {
            if(settings.vibration==VibrationCondition.None)return true;
            if(failed||!isActiveAndEnabled||!Connected||connectedPort!=portName||connectedChannelList!=channelList)
            {Status=Connecting?"ESP32の接続準備中です。":"先にESP32を接続してください。";return false;}
            return true;
        }
        public bool Begin(TrialSettings settings)
        {
            settings.ValidateTracked();
            if(!CanBegin(settings))return false;
            trial=settings.Copy();elapsed=0;SessionActive=true;
            try { if(Connected)transport.Mute();Active=false;Record("trial_start");Tick(0);return SessionActive; }
            catch(Exception e){Fault(e);return false;}
        }
        public void Tick(double time)
        {
            if(!SessionActive)return;
            elapsed=time;
            try
            {
                if(trial.vibration!=VibrationCondition.None)
                {
                    transport.Drain();if(!Connected)throw new IOException("ESP32 connection lost");
                }
                bool wanted=trial.VibrationAt(time);
                if(wanted==Active)return;
                if(wanted)foreach(int channel in channels)transport.Apply(channel,trial.vibrationFrequency,trial.vibrationAmplitude);
                else transport.Mute();
                Active=wanted;Record(wanted?"vibration_start":"vibration_stop");OutputChanged?.Invoke(Active,time);
                Status=connectedPort+" CH "+connectedChannelList+(Active?" 振動中":" 出力停止");
            }
            catch(Exception e){Fault(e);}
        }
        public void Stop(string reason="stop")
        {
            try
            {
                if(transport!=null)transport.Mute();
                if(Active){Active=false;OutputChanged?.Invoke(false,elapsed);}
                if(SessionActive)Record(reason);
            }
            catch(Exception e){Status="停止送信エラー: "+e.Message;}
            finally{Active=false;SessionActive=false;if(Connected)Status=connectedPort+" CH "+connectedChannelList+" 出力停止";}
        }
        void Update()
        {
            if(transport==null||failed)return;
            try
            {
                transport.Drain();
                if(!connectionReported&&Connected){connectionReported=true;Status=connectedPort+" CH "+connectedChannelList+" 接続済み・出力停止";}
            }
            catch(Exception e){Fault(e);}
        }
        void Record(string kind)
        {
            if(failed||!(transport is AsyncHapticTransport worker)||trial==null)return;
            string F(double value)=>value.ToString("F6",CultureInfo.InvariantCulture);
            worker.QueueLog(string.Join(",",experiment?experiment.TrialId:"",trial.vibration,kind,F(elapsed),F(Time.realtimeSinceStartupAsDouble),
                string.Join("|",channels),F(Active?trial.vibrationAmplitude:0),F(Active?trial.vibrationFrequency:0),F(trial.vibrationDelay),"queued"));
        }
        void Fault(Exception error)
        {
            if(faulting||failed)return;faulting=true;failed=true;
            Stop("fault");
            if(experiment&&experiment.Running)experiment.AbortTrial();
            Status="ESP32エラー: "+error.Message;
            // Disposal waits only on explicit disconnect / disable, never on a trial frame.
            faulting=false;
        }
        public void Disconnect()
        {
            Stop("disconnect");
            if(experiment&&experiment.Running)experiment.AbortTrial();
            try{transport?.Dispose();Status="ESP32未接続";}
            catch(Exception e){Status="切断エラー: "+e.Message;}
            finally{transport=null;connectionReported=false;}
        }
        void OnDisable(){Disconnect();}
        void OnApplicationPause(bool paused){if(paused){Stop("paused");if(experiment)experiment.AbortTrial();}}
        void OnApplicationQuit(){Disconnect();}
    }
}
