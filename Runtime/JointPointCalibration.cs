using System;
using UnityEngine;
using UnityEngine.XR;

namespace AvatarExperiments
{
    /// <summary>Three anatomical surface landmarks, recorded with the opposite controller grip.</summary>
    public sealed class JointPointCalibration : MonoBehaviour
    {
        [Tooltip("Probe offset in controller grip-local metres. Zero uses the tracked grip origin, NOT the aim ray.")]
        public Vector3 ProbeOffset;
        public bool Capturing { get; private set; }
        public bool Ready { get; private set; }
        public int Limb { get; private set; }
        public int PointCount { get; private set; }
        public float MeasuredLength { get; private set; }
        public string Status { get; private set; } = "Choose a limb. Hold it still; mark joints with the opposite controller.";
        private Transform origin;
        private Func<int,float,bool> apply;
        private readonly Vector3[] points = new Vector3[3];
        private readonly Vector3[] recent = new Vector3[16];
        private int recentCount, recentIndex;
        private bool armed;
        private float deadline, sampleAt;
        private Vector3 originPosition;
        private Quaternion originRotation;
        private GameObject probe;
        private Material probeMaterial;
        private readonly GameObject[] markers = new GameObject[3];
        public void Configure(Transform trackingOrigin, Func<int,float,bool> applyMeasurement)
        { origin=trackingOrigin; apply=applyMeasurement; }
        public void Begin(int limb)
        {
            if(limb<0 || limb>3 || origin==null)return;
            if(Vector3.Distance(origin.lossyScale,Vector3.one)>.001f)
            {Status="Tracking origin scale must be 1 for metre measurements.";return;}
            Cancel(); Limb=limb; PointCount=0; MeasuredLength=0; Ready=false;
            Capturing=true; armed=false; recentCount=recentIndex=0; deadline=Time.unscaledTime+90;
            originPosition=origin.position; originRotation=origin.rotation;
            Status=Prompt();
        }
        private string Prompt()
        {
            string[] arm={"shoulder","elbow","wrist"},leg={"hip joint","knee","ankle"};
            return $"{(Limb%2==0?"RIGHT":"LEFT")} controller: touch {(Limb<2?arm:leg)[Mathf.Min(PointCount,2)]}. Hold still, release then press trigger.";
        }
        public void Cancel()
        {
            Capturing=false; Ready=false; armed=false; ClearMarkers();
            Status="Measurement cancelled; saved values unchanged.";
        }
        private void Update()
        {
            if(!Capturing)return;
            if(Time.unscaledTime>deadline) { Cancel();Status="Measurement timed out. Start again.";return; }
            if(Vector3.Distance(origin.position,originPosition)>.001f || Quaternion.Angle(origin.rotation,originRotation)>.1f ||
                Vector3.Distance(origin.lossyScale,Vector3.one)>.001f)
            { Cancel();Status="Tracking origin moved/recentered. Start again.";return; }
            var d=InputDevices.GetDeviceAtXRNode(Limb%2==0?XRNode.RightHand:XRNode.LeftHand);
            if(!TryProbe(d,ProbeOffset,out var p))
            {
                armed=false;recentCount=0;
                if(probe!=null)probe.SetActive(false);
                Status="Controller tracking missing. Release trigger and retry.";return;
            }
            EnsureProbe();probe.SetActive(true);probe.transform.position=origin.TransformPoint(p);
            if(Time.unscaledTime>=sampleAt)
            {sampleAt=Time.unscaledTime+.02f;recent[recentIndex]=p;recentIndex=(recentIndex+1)%recent.Length;recentCount=Mathf.Min(recentCount+1,recent.Length);}
            bool pressed=d.TryGetFeatureValue(CommonUsages.triggerButton,out bool button)?button:
                d.TryGetFeatureValue(CommonUsages.trigger,out float trigger)&&trigger>.75f;
            if(!pressed){armed=true;return;}
            if(!armed)return;
            armed=false;
            if(recentCount<12){Status="Hold the probe still for 0.3 seconds, release and retry.";return;}
            Vector3 average=Vector3.zero;for(int i=0;i<recentCount;i++)average+=recent[i];average/=recentCount;
            for(int i=0;i<recentCount;i++)if(Vector3.Distance(recent[i],average)>.015f)
            {Status="Probe moved too much. Hold still, release and retry.";return;}
            Record(average);
        }
        private void Record(Vector3 localPoint)
        {
            points[PointCount]=localPoint;
            markers[PointCount]=Marker("Measured joint "+PointCount,.018f);
            markers[PointCount].transform.position=origin.TransformPoint(localPoint);
            PointCount++;recentCount=0;
            if(PointCount<3){Status=Prompt();return;}
            Capturing=false;if(probe!=null)probe.SetActive(false);
            if(!TryLength(points[0],points[1],points[2],Limb<2,out float length))
            {Ready=false;Status="Implausible joint distances. Start again; keep the measured limb still.";return;}
            MeasuredLength=length;Ready=true;
            Status=$"Measured {length*100:F1} cm. Apply to preview, then Save. Surface landmarks are approximate.";
        }
        public bool Apply()
        {
            if(!Ready || apply==null)return false;
            if(!apply(Limb,MeasuredLength))
            {Status="Not applied: fit height first, check 70-130% range / IK / active effects.";return false;}
            Ready=false;ClearMarkers();Status="Measurement applied. Check the mirror, then Save on this PC.";return true;
        }
        public static bool TryLength(Vector3 a,Vector3 b,Vector3 c,bool arm,out float length)
        {
            float first=Vector3.Distance(a,b),second=Vector3.Distance(b,c);
            length=first+second;
            float minimum=arm?.08f:.15f,maximum=arm?.65f:.85f;
            return float.IsFinite(length) && first>=minimum && second>=minimum && first<=maximum && second<=maximum;
        }
        public static bool TryProbe(InputDevice device,Vector3 offset,out Vector3 point)
        {
            point=default;
            if(!device.isValid || (device.characteristics&InputDeviceCharacteristics.Controller)==0 ||
                !device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition,out var p) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation,out var q))return false;
            if(device.TryGetFeatureValue(CommonUsages.trackingState,out InputTrackingState state) &&
                (state&(InputTrackingState.Position|InputTrackingState.Rotation))!=(InputTrackingState.Position|InputTrackingState.Rotation))return false;
            if(!float.IsFinite(q.x)||!float.IsFinite(q.y)||!float.IsFinite(q.z)||!float.IsFinite(q.w) ||
                Quaternion.Dot(q,q)<.5f)return false;
            point=p+q.normalized*offset;
            return float.IsFinite(point.x)&&float.IsFinite(point.y)&&float.IsFinite(point.z);
        }
        private void EnsureProbe(){if(probe==null)probe=Marker("Controller measurement probe",.012f);}
        private GameObject Marker(string label,float diameter)
        {
            if(probeMaterial==null)
            {
                probeMaterial=new Material(Resources.Load<Shader>("AvatarExperiments/Shaders/WorldSpaceUI"));
                probeMaterial.SetColor("_Color",new Color(.1f,1,.7f,1));
            }
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=label;
            go.transform.localScale=Vector3.one*diameter;
            var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            go.GetComponent<Renderer>().sharedMaterial=probeMaterial;
            return go;
        }
        private void ClearMarkers()
        {
            if(probe!=null)probe.SetActive(false);
            for(int i=0;i<markers.Length;i++)if(markers[i]!=null){Destroy(markers[i]);markers[i]=null;}
        }
        private void OnDisable()=>Cancel();
        private void OnDestroy(){ClearMarkers();if(probe!=null)Destroy(probe);if(probeMaterial!=null)Destroy(probeMaterial);}
    }
}
