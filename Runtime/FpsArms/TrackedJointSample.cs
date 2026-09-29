using System;
using UnityEngine;

namespace AvatarExperiments.FpsArms
{
    /// <summary>Raw measured joints for calibration, independent of the rendered IK pose.</summary>
    public sealed class TrackedJointSample
    {
        Vector3[] points;
        bool[] valid;
        float timestamp=float.NegativeInfinity;
        public void Clear(){timestamp=float.NegativeInfinity;}
        public void Capture(Vector3[] positions,bool[] validity,float time)
        {
            if(points==null||points.Length!=positions.Length){points=new Vector3[positions.Length];valid=new bool[positions.Length];}
            Array.Copy(positions,points,points.Length);Array.Copy(validity,valid,valid.Length);timestamp=time;
        }
        public bool TryGet(int index,float now,out Vector3 point)
        {
            point=default;
            if(points==null||index<0||index>=points.Length||!valid[index]||now-timestamp>.2f||now<timestamp)return false;
            point=points[index];float m=point.sqrMagnitude;return !float.IsNaN(m)&&!float.IsInfinity(m);
        }
    }
    public sealed class ElbowCalibrationHold
    {
        float elapsed;
        int count;
        Vector3 startElbow,startWrist,startHead,startForward,sumElbow,sumWrist;
        public float Progress {get;private set;}
        public Vector3 Elbow=>count>0?sumElbow/count:Vector3.zero;
        public Vector3 Wrist=>count>0?sumWrist/count:Vector3.zero;
        public void Reset(){elapsed=0;count=0;sumElbow=sumWrist=Vector3.zero;Progress=0;}
        public bool Tick(bool valid,Vector3 elbow,Vector3 wrist,Vector3 head,Vector3 forward,float dt,float duration)
        {
            if(!valid){Reset();return false;}
            if(count==0||Vector3.Distance(elbow,startElbow)>.02f||Vector3.Distance(wrist,startWrist)>.02f||
                Vector3.Distance(head,startHead)>.02f||Vector3.Angle(forward,startForward)>4)
            {Reset();startElbow=elbow;startWrist=wrist;startHead=head;startForward=forward;}
            sumElbow+=elbow;sumWrist+=wrist;count++;elapsed+=Mathf.Clamp(dt,0,.05f);
            Progress=Mathf.Clamp01(elapsed/duration);
            return elapsed>=duration&&count>=20;
        }
    }
}
