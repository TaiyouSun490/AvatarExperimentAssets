using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarExperiments.VirtualHand.Editor
{
    public static class TabletopConditionVibrationVerification
    {
        sealed class Output : ITabletopHapticTransport
        {
            public bool Connected{get;private set;}=true;
            public bool muted=true,fail;
            public int applies;
            public readonly HashSet<int> channels=new HashSet<int>();
            public void Prepare(int channel){muted=true;}
            public void Apply(int channel,float frequency,float amplitude)
            {if(fail)throw new InvalidOperationException("Injected write failure");if(frequency!=70||Mathf.Abs(amplitude-.2f)>.00001f)throw new Exception("Condition output changed after settings mutation");channels.Add(channel);applies++;muted=false;}
            public void Mute(){muted=true;}
            public void Drain(){}
            public void Dispose(){Connected=false;muted=true;}
        }
        public static void Run(TabletopExperiment experiment,Action<bool,string> check)
        {
            var bridge=experiment.vibration;check(bridge,"Condition vibration bridge exists");
            var previous=experiment.settings.Copy();
            experiment.ResetTrial();bridge.Disconnect();
            var settings=new TrialSettings{preparation=1,induction=2,melting=2,observation=1,vibrationDelay=.5f,vibrationAmplitude=.2f,vibrationFrequency=70};
            check(bridge.Begin(settings)&&!bridge.Active,"No-vibration condition works without a connected device");bridge.Stop();
            double[] times={0,1,2.9,3,3.5,4.9,5,5.4,5.5,5.9,6};
            bool[][] expected={
                new[]{false,false,false,false,false,false,false,false,false,false,false},
                new[]{false,false,false,true,true,true,false,false,false,false,false},
                new[]{false,false,false,false,true,true,true,true,false,false,false},
                new[]{true,true,true,true,true,true,true,true,true,true,false}
            };
            foreach(VibrationCondition condition in Enum.GetValues(typeof(VibrationCondition)))
            {
                var output=new Output();bridge.TransportFactory=()=>output;bridge.Connect();
                settings.vibration=condition;check(bridge.Begin(settings),condition+": starts with prepared output");
                settings.vibration=VibrationCondition.None;settings.vibrationAmplitude=1;
                for(int i=0;i<times.Length;i++)
                {
                    bridge.Tick(times[i]);
                    check(bridge.Active==expected[(int)condition][i],condition+": expected output at "+times[i].ToString("F1")+" s");
                }
                check(output.applies==(condition==VibrationCondition.None?0:4),condition+": sends only state transitions to CH0..3");
                if(condition!=VibrationCondition.None)check(output.channels.SetEquals(new[]{0,1,2,3}),condition+": correct channel set");
                bridge.Stop("test_stop");check(output.muted&&!bridge.Active&&!bridge.SessionActive,condition+": stops and clears session");
                bridge.Disconnect();settings.vibrationAmplitude=.2f;
            }
            var active=new Output();bridge.TransportFactory=()=>active;bridge.Connect();
            settings.vibration=VibrationCondition.ThroughoutTrial;experiment.settings=settings.Copy();
            experiment.StartTrial();check(experiment.Running&&bridge.Active,"ThroughoutTrial begins at trial start, including preparation");
            experiment.Advance(settings.End);check(experiment.Phase==TrialPhase.Completed&&active.muted&&!bridge.Active,"Trial completion stops continuous output");
            experiment.ResetTrial();experiment.StartTrial();experiment.AbortTrial();
            check(active.muted&&!bridge.Active&&!bridge.SessionActive,"Abort immediately mutes continuous output");
            experiment.ResetTrial();experiment.StartTrial();experiment.ResetTrial();
            check(active.muted&&!bridge.Active&&!bridge.SessionActive,"Reset immediately mutes continuous output");
            active.fail=true;experiment.StartTrial();
            check(experiment.Phase==TrialPhase.Aborted&&!bridge.Active,"Transport failure aborts trial instead of leaving output active");
            experiment.ResetTrial();bridge.Disconnect();bridge.TransportFactory=null;experiment.settings=previous;
            settings.vibration=VibrationCondition.MeltDelayed;settings.observation=.1f;
            bool rejected=false;try{settings.ValidateVibration();}catch(ArgumentException){rejected=true;}
            check(rejected,"Delayed condition rejects a truncated observation tail");
        }
    }
}
