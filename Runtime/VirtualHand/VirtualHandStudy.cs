using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AvatarExperiments.VirtualHand
{
    public enum StudyStage { Setup, Baseline, AwaitStimulus, Stimulation, PostMeasurement, Ratings, Rest, Completed, Aborted }
    public enum ConditionOrder { Listed, SeededShuffle, RotatedCounterbalance }
    [Serializable] public sealed class StudyCondition
    {
        public string code="condition";
        public TrialSettings stimulus=new TrialSettings();
    }
    [Serializable] public sealed class StudyProtocol
    {
        public string protocolId="virtual-hand-pilot-v1";
        [TextArea] public string brushingInstructions="Use the same anatomical region, brush, contact pressure and stroke pace across conditions. Record deviations.";
        public ConditionOrder order=ConditionOrder.RotatedCounterbalance;
        public int seed=12345, counterbalanceIndex;
        [Range(1,20)] public int repetitions=1;
        [Min(0)] public float restSeconds=30;
        public DriftSettings drift=new DriftSettings();
        public List<StudyCondition> conditions=new List<StudyCondition>
        {
            new StudyCondition{code="sync_no_melt_17cm",stimulus=new TrialSettings{synchrony=Synchrony.Synchronous,enableMelting=false,handOffset=new Vector3(.17f,0,0),induction=120,melting=.1f,observation=0}},
            new StudyCondition{code="async_no_melt_17cm",stimulus=new TrialSettings{synchrony=Synchrony.Asynchronous,enableMelting=false,handOffset=new Vector3(.17f,0,0),induction=120,melting=.1f,observation=0}}
        };
        public StudyProtocol Copy()=>JsonUtility.FromJson<StudyProtocol>(JsonUtility.ToJson(this));
        public void Validate()
        {
            drift.Validate();
            if(conditions==null||conditions.Count==0||repetitions<1||repetitions>20||!float.IsFinite(restSeconds)||restSeconds<0||!Enum.IsDefined(typeof(ConditionOrder),order))
                throw new ArgumentException("Invalid protocol.");
            var codes=new HashSet<string>();
            foreach(var condition in conditions)
            {if(condition==null||string.IsNullOrWhiteSpace(condition.code)||!codes.Add(condition.code))throw new ArgumentException("Condition codes must be unique.");condition.stimulus.ValidateTracked();}
        }
        public int[] Schedule()
        {
            Validate();var result=new List<int>();var random=new System.Random(seed);
            for(int repetition=0;repetition<repetitions;repetition++)
            {
                var block=new List<int>();
                for(int i=0;i<conditions.Count;i++)block.Add(order==ConditionOrder.RotatedCounterbalance?((i+counterbalanceIndex)%conditions.Count+conditions.Count)%conditions.Count:i);
                if(order==ConditionOrder.SeededShuffle)for(int i=block.Count-1;i>0;i--){int j=random.Next(i+1);int value=block[i];block[i]=block[j];block[j]=value;}
                result.AddRange(block);
            }
            return result.ToArray();
        }
    }
    [Serializable] public sealed class StudyTrialResult
    {
        public int orderIndex;
        public string conditionCode,trialId,status="pending",reason,operatorNotes;
        public bool practice,valid,hasTowardVirtualAxis;
        public TrialSettings settings;
        public Vector3 physicalTarget,virtualTarget,measurementAxis,driftVector;
        public float driftMetres,towardVirtualMetres,targetCheckErrorMetres,alignmentErrorMetres;
        public DriftEstimate baseline,post;
        public int[] ratings;
        public double baselineStart,baselineEnd,stimulusEnd,postStart,postEnd,responseEnd;
    }
    [Serializable] public sealed class StudyRecord
    {
        public int schemaVersion=1;
        public string runId,participantCode,sessionCode,utcStart,status="running",input,unityVersion,reason;
        public StudyProtocol protocol;
        public int[] schedule;
        public List<StudyTrialResult> trials=new List<StudyTrialResult>();
        public string[] ratingIds={"ownership","touch_location","touch_causation","melting_sensation","softness","spreading"};
        public string[] ratingPrompts=VirtualHandStudy.ParticipantQuestions;
        public string ratingLanguage="en";
        public string ratingScale="1 completely disagree .. 7 completely agree; custom adapted/exploratory items, not a validated questionnaire";
    }
    [DisallowMultipleComponent]
    public sealed class VirtualHandStudy : MonoBehaviour
    {
        public TabletopExperiment experiment;
        public DriftPresenter presenter;
        public StudyProtocol protocol=new StudyProtocol();
        public Transform virtualTarget;
        [Tooltip("Actual position of the right index fingertip, captured with the calibrated brush; XR tracking-space metres.")]
        public Vector3 physicalTarget;
        public bool PhysicalTargetConfirmed {get;private set;}
        public bool PointingTipConfirmed {get;private set;}
        public bool setupChecked,loggingRequired=true;
        [Min(.001f)] public float targetCheckTolerance=.015f;
        public float TargetCheckError {get;private set;}
        public string operatorNotes="";
        public StudyStage Stage {get;private set;}=StudyStage.Setup;
        public string Status {get;private set;}="Confirm alignment, measurement target and physical setup.";
        public bool Active=>Stage>=StudyStage.Baseline&&Stage<=StudyStage.Rest;
        public bool AllowStimulusStart=>Stage==StudyStage.AwaitStimulus;
        public DriftMeasurement Measurement {get;private set;}
        public string RecordPath {get;private set;}
        public float RestRemaining=>Mathf.Max(0,(float)(restUntil-Time.realtimeSinceStartupAsDouble));
        public StudyRecord RecordData=>record;
        StudyRecord record;
        StudyProtocol frozen;
        StudyTrialResult current;
        int index;
        bool practice,finishing;
        TabletopHeadTracking head;
        double measurementStarted,restUntil;
        Vector3 originPosition,originScale;
        Vector3 confirmedPointingOffset;
        Quaternion originRotation;
        static readonly string[] questions={"仮想の手が自分の手のように感じた","触られた感覚が仮想の手の位置にあるように感じた","見えている刺激が触覚を起こしているように感じた","自分の手が溶けるように感じた","自分の手が柔らかくなるように感じた","自分の手が広がるように感じた"};
        public static string[] Questions=>(string[])questions.Clone();
        public static string[] ParticipantQuestions=>new[]{"The virtual hand felt like my own hand.","The touch felt located at the virtual hand.","The visible stimulus seemed to cause the touch.","My own hand felt as if it was melting.","My own hand felt softer.","My own hand felt as if it was spreading."};
        void OnEnable(){if(experiment&&experiment.trackedBrush)experiment.trackedBrush.CalibrationInvalidated+=CalibrationLost;}
        public void CapturePhysicalTarget()
        {
            if(Active)return;
            var brush=experiment.trackedBrush;
            if(!brush.Ready){Status="Calibrate the brush and hand first.";return;}
            if(!virtualTarget){Status="Assign the virtual anatomical target first.";return;}
            TargetCheckError=Vector3.Distance(brush.MapToVisual(brush.RawTip),virtualTarget.position);
            if(!float.IsFinite(TargetCheckError)||TargetCheckError>targetCheckTolerance)
            {PhysicalTargetConfirmed=false;Status=$"Independent target mismatch: {TargetCheckError*1000:F1} mm. Check the actual tip, model target and hand alignment.";return;}
            physicalTarget=brush.trackingSpace.InverseTransformPoint(brush.RawTip);PhysicalTargetConfirmed=true;
            Status="Physical measurement target recorded. Keep the real hand fixed.";
        }
        public void ConfirmPointingTip(Vector3 measuredOffset)
        {
            if(Active||!float.IsFinite(measuredOffset.sqrMagnitude)||measuredOffset.magnitude>.6f)return;
            protocol.drift.pointingTipOffset=measuredOffset;confirmedPointingOffset=measuredOffset;PointingTipConfirmed=true;
        }
        public void BeginStudy()=>Begin(false);
        public void BeginPractice()=>Begin(true);
        void Begin(bool isPractice)
        {
            if(Active)return;
            bool created=false;
            try
            {
                protocol.Validate();
                if(experiment.Running)throw new InvalidOperationException("Stop the standalone stimulus before beginning a study.");
                if(!experiment.trackedBrush.Ready||!PhysicalTargetConfirmed||!virtualTarget||!setupChecked)
                    throw new InvalidOperationException("Confirm calibration, physical fingertip reference, virtual target and setup check before starting.");
                if(protocol.drift.method==DriftMethod.InvisibleControllerPointing&&(!PointingTipConfirmed||protocol.drift.pointingTipOffset!=confirmedPointingOffset))
                    throw new InvalidOperationException("Confirm the left controller's pointing-tip offset on a known point first.");
                if(loggingRequired&&!experiment.writeLog)throw new InvalidOperationException("Enable logs before starting the study.");
                head=experiment.GetComponentInChildren<TabletopHeadTracking>();
                if(!experiment.desktopPreview&&(!head||!head.Tracked))throw new InvalidOperationException("A tracked HMD is required for the VR study.");
                frozen=protocol.Copy();practice=isPractice;index=0;
                var space=experiment.trackedBrush.trackingSpace;originPosition=space.position;originRotation=space.rotation;originScale=space.lossyScale;
                record=new StudyRecord{runId=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N").Substring(0,8),
                    participantCode=experiment.participantCode,sessionCode=experiment.sessionCode,utcStart=DateTime.UtcNow.ToString("O"),
                    unityVersion=Application.unityVersion,input=experiment.desktopPreview?"Desktop simulation; not participant drift data":"XR controller + physical brush",
                    protocol=frozen,schedule=isPractice?new[]{0}:frozen.Schedule()};
                current=null;RecordPath=null;created=true;
                experiment.ResetTrial();experiment.keyboardControls=false;experiment.controllerControls=false;
                experiment.trackedBrush.calibrationPreview=false;experiment.trackedBrush.ConfigurationLocked=true;
                string directory=experiment.LogDirectoryOverride??Path.Combine(Application.persistentDataPath,"VirtualHandLogs");Directory.CreateDirectory(directory);
                RecordPath=Path.Combine(directory,record.runId+".study.json");Save();PrepareTrial();
            }
            catch(Exception error){if(created)Abort("Study start failed: "+error.Message);else Status="Study start failed: "+error.Message;}
        }
        void PrepareTrial()
        {
            experiment.ResetTrial();
            var condition=frozen.conditions[record.schedule[index]];experiment.settings=condition.stimulus.Copy();experiment.conditionCode=condition.code;
            experiment.placement.Apply(experiment.settings.handOffset,experiment.settings.handYaw);
            current=new StudyTrialResult{orderIndex=index,conditionCode=condition.code,settings=condition.stimulus.Copy(),practice=practice,
                physicalTarget=physicalTarget,virtualTarget=experiment.trackedBrush.trackingSpace.InverseTransformPoint(virtualTarget.position),
                measurementAxis=frozen.drift.axisInTrackingSpace.normalized,targetCheckErrorMetres=TargetCheckError,
                alignmentErrorMetres=experiment.trackedBrush.AlignmentError};record.trials.Add(current);
            Stage=StudyStage.Baseline;BeginMeasurement();
        }
        void BeginMeasurement()
        {
            experiment.vibration?.Stop("drift_measurement");experiment.ball.gameObject.SetActive(false);
            Measurement=new DriftMeasurement(frozen.drift,current.physicalTarget,unchecked(frozen.seed+index*7919+(Stage==StudyStage.Baseline?0:104729)));
            measurementStarted=Time.realtimeSinceStartupAsDouble;
            if(Stage==StudyStage.Baseline)current.baselineStart=measurementStarted;else current.postStart=measurementStarted;
            presenter.Begin(this);Status="仮想手を隠しています。実際の "+frozen.drift.targetLabel+" があると感じる位置を回答してください。";
            Save();
        }
        public void SubmitPoint(Vector3 trackingSpacePoint)=>Submit(()=>Measurement.Record(trackingSpacePoint,Time.realtimeSinceStartupAsDouble-measurementStarted));
        public void SubmitMarker()=>Submit(()=>Measurement.ConfirmMarker(Time.realtimeSinceStartupAsDouble-measurementStarted));
        public void SubmitRuler(float number)=>Submit(()=>Measurement.ConfirmRuler(number,Time.realtimeSinceStartupAsDouble-measurementStarted));
        public void SubmitJudgement(bool probeIsPositive)=>Submit(()=>Measurement.Judge(probeIsPositive,Time.realtimeSinceStartupAsDouble-measurementStarted));
        void Submit(Action action)
        {
            if(Stage!=StudyStage.Baseline&&Stage!=StudyStage.PostMeasurement)return;
            try
            {
                action();
                var estimate=Measurement.Estimate();
                if(Stage==StudyStage.Baseline)current.baseline=estimate;else current.post=estimate;
                Save();
                if(!Measurement.Complete){presenter.Refresh();return;}
                if(!estimate.valid){Abort(estimate.reason);return;}
                if(Stage==StudyStage.Baseline)
                {current.baselineEnd=Time.realtimeSinceStartupAsDouble;Stage=StudyStage.AwaitStimulus;Status="Baseline recorded. Experimenter: prepare to stroke, then start.";presenter.HoldBlank();}
                else
                {
                    current.postEnd=Time.realtimeSinceStartupAsDouble;
                    current.driftMetres=DriftMeasurement.Drift(current.baseline,current.post);
                    current.driftVector=current.post.meanPosition-current.baseline.meanPosition;
                    var displacement=current.virtualTarget-current.physicalTarget;
                    float alongAxis=Vector3.Dot(displacement,current.measurementAxis);
                    current.hasTowardVirtualAxis=Measurement.Settings.method==DriftMethod.InvisibleControllerPointing?displacement.magnitude>.001f:Mathf.Abs(alongAxis)>.001f;
                    current.towardVirtualMetres=Measurement.Settings.method==DriftMethod.InvisibleControllerPointing?
                        Vector3.Dot(current.driftVector,displacement.normalized):current.driftMetres*Mathf.Sign(alongAxis);
                    current.valid=true;Stage=StudyStage.Ratings;presenter.BeginRatings();Status="Answer each item based on this trial.";
                }
                Save();
            }
            catch(ArgumentException error){Status="Response was not recorded: "+error.Message;}
            catch(Exception error){Abort("Measurement failed: "+error.Message);}
        }
        public void StartStimuli()
        {
            if(Stage!=StudyStage.AwaitStimulus)return;
            try
            {
                presenter.End();experiment.hand.skin.enabled=true;experiment.StartTrial();
                if(!experiment.Running){presenter.HoldBlank();Status=experiment.Status;return;}
                current.trialId=experiment.TrialId;Stage=StudyStage.Stimulation;Status=frozen.brushingInstructions;Save();
            }
            catch(Exception error){Abort(error.Message);}
        }
        public void SubmitRatings(int[] values)
        {
            if(Stage!=StudyStage.Ratings)return;
            if(values==null||values.Length!=questions.Length||Array.Exists(values,v=>v<1||v>7)){Status="Answer all six items (1..7).";return;}
            current.ratings=(int[])values.Clone();current.responseEnd=Time.realtimeSinceStartupAsDouble;current.status="completed";current.operatorNotes=operatorNotes;
            Stage=StudyStage.Rest;restUntil=Time.realtimeSinceStartupAsDouble+frozen.restSeconds;presenter.HoldBlank();Status="Rest. Continue after the interval and real-hand placement check.";Save();
        }
        public void NextTrial(bool realHandUnmoved)
        {
            if(Stage!=StudyStage.Rest||RestRemaining>0)return;
            if(!realHandUnmoved){Abort("Real hand moved; repeat calibration and begin a new run.");return;}
            index++;
            if(index>=record.schedule.Length)
            {
                Stage=StudyStage.Completed;record.status="completed";Save();Finish();Status=practice?"Practice complete. Begin the actual study explicitly.":"Study complete.";
            }
            else PrepareTrial();
        }
        void Update()
        {
            if(!Active)return;
            if(!experiment.desktopPreview&&(!head||!head.Tracked)){Abort("HMD tracking lost.");return;}
            var space=experiment.trackedBrush.trackingSpace;
            if(Vector3.Distance(space.position,originPosition)>.001f||Quaternion.Angle(space.rotation,originRotation)>.1f||Vector3.Distance(space.lossyScale,originScale)>.001f)
            {Abort("XR origin changed; measurements are not comparable.");return;}
            if(!experiment.placement.Unchanged){Abort("Virtual hand was moved outside the scheduled offset.");return;}
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){Abort("Operator abort");return;}
            if(Stage==StudyStage.AwaitStimulus&&Keyboard.current!=null&&Keyboard.current.spaceKey.wasPressedThisFrame)StartStimuli();
            if(Stage==StudyStage.Stimulation)
            {
                if(experiment.Phase==TrialPhase.Aborted){Abort(experiment.Status);return;}
                if(experiment.Phase==TrialPhase.Completed){current.stimulusEnd=Time.realtimeSinceStartupAsDouble;Stage=StudyStage.PostMeasurement;BeginMeasurement();}
            }
        }
        void Save()
        {
            if(record==null||string.IsNullOrEmpty(RecordPath))return;
            string temporary=RecordPath+".tmp";File.WriteAllText(temporary,JsonUtility.ToJson(record,true));
            if(File.Exists(RecordPath))File.Replace(temporary,RecordPath,null);else File.Move(temporary,RecordPath);
            var summary=new StringBuilder("run,participant,session,trial,condition,practice,status,valid,method,pre_m,post_m,drift_m,toward_virtual_m,reason\n");
            string Q(string value)=>"\""+(value??"").Replace("\"","\"\"")+"\"";
            string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
            foreach(var result in record.trials)
                summary.AppendLine(string.Join(",",Q(record.runId),Q(record.participantCode),Q(record.sessionCode),Q(result.trialId),Q(result.conditionCode),
                    result.practice,result.status,result.valid,record.protocol.drift.method,result.baseline!=null?F(result.baseline.coordinateMetres):"",
                    result.post!=null?F(result.post.coordinateMetres):"",result.valid?F(result.driftMetres):"",result.valid&&result.hasTowardVirtualAxis?F(result.towardVirtualMetres):"",Q(result.reason)));
            File.WriteAllText(Path.ChangeExtension(RecordPath,".csv"),summary.ToString());
        }
        public void Abort(string reason)
        {
            if(finishing)return;
            // A stop button or failed later start must not rewrite an already completed run.
            if(!Active&&(record==null||record.status!="running")){Finish();Status=reason;return;}
            Stage=StudyStage.Aborted;Status=reason;
            if(record!=null){record.status="aborted";record.reason=reason;}
            if(current!=null&&current.status!="completed"){current.status="aborted";current.valid=false;current.reason=reason;current.operatorNotes=operatorNotes;}
            Finish();try{Save();}catch(Exception error){Debug.LogError("Study save failed: "+error.Message);}
        }
        void Finish()
        {
            finishing=true;
            if(experiment){experiment.AbortTrial();experiment.trackedBrush.ConfigurationLocked=false;experiment.trackedBrush.calibrationPreview=false;}
            if(presenter)presenter.End();finishing=false;
        }
        void CalibrationLost(string reason){PhysicalTargetConfirmed=false;if(Active)Abort(reason);}
        void OnDisable(){if(Active)Abort("Study disabled");if(experiment&&experiment.trackedBrush)experiment.trackedBrush.CalibrationInvalidated-=CalibrationLost;}
        void OnApplicationPause(bool paused){if(paused&&Active)Abort("Application paused");}
    }
}
