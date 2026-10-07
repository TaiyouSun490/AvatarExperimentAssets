using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AvatarExperiments.VirtualHand.Editor
{
    static class VirtualHandReviewVerification
    {
        public static void Run(TabletopExperiment e,Action<bool,string> check)
        {
            var settings=new DriftSettings{method=DriftMethod.MarkerAdjustment,repetitions=3};
            var measurement=new DriftMeasurement(settings,Vector3.zero,12);
            settings.repetitions=1;measurement.Settings.repetitions=1;
            bool rejected=false;try{measurement.Record(Vector3.right,0);}catch(ArgumentException){rejected=true;}
            check(rejected&&measurement.Count==0,"Marker method rejects a pointing response without consuming an answer");
            measurement.ConfirmMarker(1);
            var exposed=measurement.Estimate();exposed.responses[0].coordinateMetres=100;
            check(!measurement.Complete&&Mathf.Abs(measurement.Estimate().responses[0].coordinateMetres)<1,"Settings and returned responses cannot mutate the measurement");
            var ruler=new DriftMeasurement(new DriftSettings{method=DriftMethod.RandomizedRuler,halfRange=.205f},Vector3.zero,2);
            rejected=false;try{ruler.ConfirmRuler(ruler.RulerZero+20.3f,0);}catch(ArgumentException){rejected=true;}
            check(rejected&&ruler.Count==0,"Ruler rejects answers outside the actually rendered centimetre ticks");
            var delayedProtocol=new StudyProtocol();delayedProtocol.conditions[1].stimulus.preparation=0;
            rejected=false;try{delayedProtocol.Validate();}catch(ArgumentException){rejected=true;}
            check(rejected,"Study rejects an asynchronous condition that would start induction without enough pose history");

            var study=e.study;var brush=e.trackedBrush;
            void Prepare()
            {
                if(study.Active)study.Abort("Synthetic test cleanup");
                e.ResetTrial();e.keyboardControls=true;e.controllerControls=false;
                study.protocol=new StudyProtocol{restSeconds=0};
                study.protocol.conditions=new List<StudyCondition>{new StudyCondition{code="review",stimulus=new TrialSettings{preparation=0,induction=.2f,melting=.1f,observation=0,enableMelting=false,handOffset=Vector3.right*.17f}}};
                brush.Sample(brush.SampleTime+.01,new Pose(brush.trackingSpace.InverseTransformPoint(brush.MapToReal(study.virtualTarget.position)),Quaternion.identity));
                study.CapturePhysicalTarget();study.setupChecked=true;
            }
            void Baseline()
            {
                study.BeginStudy();check(study.Stage==StudyStage.Baseline,"Review scenario begins with a hidden baseline");
                for(int i=0;i<3;i++){study.Measurement.Adjust(.01f-study.Measurement.Coordinate);study.SubmitMarker();}
            }
            void ToRatings()
            {
                Baseline();study.StartStimuli();e.Advance(1);study.SendMessage("Update");
                for(int i=0;i<3;i++){study.Measurement.Adjust(.03f-study.Measurement.Coordinate);study.SubmitMarker();}
                check(study.Stage==StudyStage.Ratings,"Review scenario reaches post-measurement ratings");
            }
            Prepare();Baseline();
            study.protocol.conditions[0].stimulus.enableMelting=true;e.settings.enableMelting=true;e.settings.handOffset=Vector3.left;
            e.participantCode="changed_after_begin";study.RecordData.protocol.conditions[0].stimulus.enableMelting=true;
            study.StartStimuli();
            check(study.Stage==StudyStage.Stimulation&&!e.ActiveSettings.enableMelting&&e.ActiveSettings.handOffset==Vector3.right*.17f&&e.participantCode==study.RecordData.participantCode,
                "Stimuli and identity use the frozen run values despite changes during baseline");
            e.ResetTrial();study.SendMessage("Update");
            check(study.Stage==StudyStage.Aborted&&e.keyboardControls&&!e.controllerControls,"Reset during stimulation aborts the study and restores its prior controls");

            Prepare();study.BeginStudy();study.physicalTarget+=Vector3.right*.01f;study.SubmitMarker();
            check(study.Stage==StudyStage.Aborted&&!study.RecordData.trials[0].valid,"Changing the physical reference invalidates the run before accepting a response");
            Prepare();study.BeginStudy();e.placement.Apply(Vector3.right*.3f,0);study.SubmitMarker();
            check(study.Stage==StudyStage.Aborted,"An unscheduled placement API call cannot silently change baseline geometry");
            Prepare();study.protocol.conditions[0].stimulus.appearance=MeltAppearance.MeltingTest;study.BeginStudy();
            check(!study.Active&&!study.presenter.Active,"Unavailable condition material is rejected before baseline");
            Prepare();Baseline();var originalMaterial=e.hand.viscousMaterial;
            e.hand.viscousMaterial=null;study.StartStimuli();
            check(study.Stage==StudyStage.Aborted&&!study.presenter.Active,"Failed stimulus startup aborts instead of leaving a non-retryable waiting stage");
            e.hand.viscousMaterial=originalMaterial;

            Prepare();ToRatings();
            string csv=Path.ChangeExtension(study.RecordPath,".csv"),blocked=csv+".tmp",previous=File.ReadAllText(csv);
            Directory.CreateDirectory(blocked);
            try
            {
                study.SubmitRatings(new[]{4,4,4,1,1,1});
                check(study.Stage==StudyStage.Aborted&&!study.presenter.Active&&!e.vibration.Active&&e.keyboardControls,
                    "A rating-save I/O failure aborts, stops output, restores display and controls");
                check(File.ReadAllText(csv)==previous,"Failed CSV replacement preserves the previous complete file");
            }
            finally{Directory.Delete(blocked);}
            Prepare();ToRatings();study.SubmitRatings(new[]{4,4,4,1,1,1});
            blocked=Path.ChangeExtension(study.RecordPath,".csv")+".tmp";Directory.CreateDirectory(blocked);
            try
            {
                study.NextTrial(true);
                check(study.Stage==StudyStage.Aborted&&study.RecordData.status=="aborted"&&!study.presenter.Active,
                    "A final-session save failure is not reported as a completed session");
            }
            finally{Directory.Delete(blocked);}
            Prepare();study.BeginStudy();study.presenter.enabled=false;
            check(study.Stage==StudyStage.Aborted&&!study.presenter.Active,"Disabling the measurement presenter aborts the active study");
            study.presenter.enabled=true;e.ResetTrial();
        }
    }
}
