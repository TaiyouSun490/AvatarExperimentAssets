using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarExperiments.VirtualHand
{
    public enum DriftMethod { InvisibleControllerPointing, MarkerAdjustment, AlternatingStaircase, RandomizedRuler }
    [Serializable]
    public sealed class DriftSettings
    {
        public DriftMethod method = DriftMethod.MarkerAdjustment;
        [Range(1, 20)] public int repetitions = 3;
        [Min(.05f)] public float halfRange = .20f;
        [Range(20, 200)] public int staircaseResponses = 80;
        [Min(.001f)] public float coarseStep = .04f, fineStep = .01f;
        [Range(2, 12)] public int requiredReversals = 4;
        public Vector3 axisInTrackingSpace = Vector3.right;
        [Tooltip("Grip-local position used to point with the left controller. Confirm on a known point before starting.")]
        public Vector3 pointingTipOffset;
        public string targetLabel = "right index fingertip";
        public void Validate()
        {
            if(!Enum.IsDefined(typeof(DriftMethod),method)||repetitions<1||repetitions>20||!float.IsFinite(halfRange)||halfRange<.05f||halfRange>1 ||
                !float.IsFinite(axisInTrackingSpace.sqrMagnitude)||axisInTrackingSpace.magnitude<.1f||!float.IsFinite(pointingTipOffset.sqrMagnitude)||
                staircaseResponses<20||staircaseResponses>200||requiredReversals<2||requiredReversals>12||
                !float.IsFinite(coarseStep)||!float.IsFinite(fineStep)||fineStep<=0||coarseStep<=0||coarseStep>halfRange||fineStep>halfRange)
                throw new ArgumentException("Invalid drift measurement settings.");
        }
        public DriftSettings Copy()=>JsonUtility.FromJson<DriftSettings>(JsonUtility.ToJson(this));
    }
    [Serializable]
    public sealed class DriftResponse
    {
        public int index, staircase=-1, direction, rulerZero;
        public double elapsed;
        public Vector3 positionInTrackingSpace;
        public float coordinateMetres, stimulusCoordinateMetres, initialCoordinateMetres, rulerShiftMetres, rulerReportedNumber;
        public bool reversal, boundary;
    }
    [Serializable]
    public sealed class DriftEstimate
    {
        public DriftMethod method;
        public bool valid;
        public string reason;
        public Vector3 meanPosition;
        public float coordinateMetres, sdMetres, coarseEstimateMetres, fineEstimateMetres;
        public int coarseReversals, fineReversals;
        public List<DriftResponse> responses = new List<DriftResponse>();
    }
    /// <summary>Coordinates are in the unchanged XR tracking frame, never in the displaced virtual hand.</summary>
    public sealed class DriftMeasurement
    {
        public DriftSettings Settings { get; }
        public Vector3 Reference { get; }
        public Vector3 Axis { get; }
        public float Coordinate { get; private set; }
        public float InitialCoordinate { get; private set; }
        public int RulerZero { get; private set; }
        public float RulerShift { get; private set; }
        public bool Complete => responses.Count >= (Settings.method==DriftMethod.AlternatingStaircase?Settings.staircaseResponses:Settings.repetitions);
        public int Count=>responses.Count;
        public Vector3 ProbePosition => Reference+Axis*Coordinate;
        readonly System.Random random;
        readonly List<DriftResponse> responses = new List<DriftResponse>();
        readonly float[] stairs = new float[2];
        readonly int[] lastDirection = new int[2];
        readonly List<float>[] reversals = {new List<float>(),new List<float>()};
        double lastTime=-1;
        public DriftMeasurement(DriftSettings settings, Vector3 reference, int seed)
        {
            settings.Validate();if(!float.IsFinite(reference.sqrMagnitude))throw new ArgumentException("Invalid physical reference.");
            Settings=settings.Copy();Reference=reference;Axis=settings.axisInTrackingSpace.normalized;random=new System.Random(seed);Next();
        }
        void Next()
        {
            Coordinate=Settings.method==DriftMethod.AlternatingStaircase?stairs[Count%2]:(float)(random.NextDouble()*2-1)*Settings.halfRange;
            InitialCoordinate=Coordinate;
            // The ruler origin AND labels change between responses; no correct location is displayed.
            RulerShift=(float)(random.NextDouble()-.5)*.1f;RulerZero=random.Next(20,80);
        }
        public void Adjust(float metres)
        {
            if(Settings.method!=DriftMethod.MarkerAdjustment||Complete||!float.IsFinite(metres))return;
            Coordinate=Mathf.Clamp(Coordinate+metres,-Settings.halfRange,Settings.halfRange);
        }
        public void ConfirmMarker(double time)=>Record(Reference+Axis*Coordinate,time);
        public void ConfirmRuler(float number,double time)
        {
            if(Settings.method!=DriftMethod.RandomizedRuler)throw new InvalidOperationException("Not a ruler measurement.");
            float coordinate=(number-RulerZero)*.01f+RulerShift;
            if(!float.IsFinite(number)||Mathf.Abs(coordinate-RulerShift)>Settings.halfRange)throw new ArgumentOutOfRangeException(nameof(number));
            Record(Reference+Axis*coordinate,time);
            responses[responses.Count-1].rulerReportedNumber=number;
        }
        public void Record(Vector3 response,double time)
        {
            if(Complete||!double.IsFinite(time)||time<0||time<lastTime||!float.IsFinite(response.sqrMagnitude))throw new ArgumentException("Invalid drift response.");
            if(Settings.method==DriftMethod.AlternatingStaircase)throw new InvalidOperationException("Use a left/right judgement.");
            float value=Vector3.Dot(response-Reference,Axis);
            responses.Add(new DriftResponse{index=Count,elapsed=time,positionInTrackingSpace=response,coordinateMetres=value,
                stimulusCoordinateMetres=Coordinate,initialCoordinateMetres=InitialCoordinate,rulerZero=RulerZero,rulerShiftMetres=RulerShift,
                boundary=(Settings.method==DriftMethod.MarkerAdjustment&&Mathf.Abs(value)>=Settings.halfRange-.00001f)||
                    (Settings.method==DriftMethod.RandomizedRuler&&Mathf.Abs(value-RulerShift)>=Settings.halfRange-.00001f)});
            lastTime=time;if(!Complete)Next();
        }
        /// <param name="probeIsPositive">True means the probe is on the positive-axis side (right for default lateral axis) of the felt finger.</param>
        public void Judge(bool probeIsPositive,double time)
        {
            if(Settings.method!=DriftMethod.AlternatingStaircase||Complete||!double.IsFinite(time)||time<0||time<lastTime)throw new ArgumentException("Invalid staircase response.");
            int stair=Count%2, direction=probeIsPositive?-1:1;
            bool reversal=lastDirection[stair]!=0&&lastDirection[stair]!=direction;
            if(reversal)reversals[stair].Add(Coordinate);
            float next=Coordinate+direction*(stair==0?Settings.coarseStep:Settings.fineStep);
            responses.Add(new DriftResponse{index=Count,staircase=stair,direction=direction,elapsed=time,coordinateMetres=Coordinate,
                stimulusCoordinateMetres=Coordinate,initialCoordinateMetres=InitialCoordinate,positionInTrackingSpace=ProbePosition,
                reversal=reversal,boundary=Mathf.Abs(next)>Settings.halfRange});
            lastDirection[stair]=direction;stairs[stair]=Mathf.Clamp(next,-Settings.halfRange,Settings.halfRange);lastTime=time;if(!Complete)Next();
        }
        public DriftEstimate Estimate()
        {
            var result=new DriftEstimate{method=Settings.method,responses=new List<DriftResponse>(responses),valid=Complete,reason=Complete?"":"Incomplete"};
            if(responses.Count==0)return result;
            if(Settings.method==DriftMethod.AlternatingStaircase)
            {
                result.coarseReversals=reversals[0].Count;result.fineReversals=reversals[1].Count;
                result.coarseEstimateMetres=LastMean(reversals[0]);result.fineEstimateMetres=LastMean(reversals[1]);
                result.coordinateMetres=result.fineEstimateMetres;result.meanPosition=Reference+Axis*result.coordinateMetres;
                if(result.coarseReversals<Settings.requiredReversals||result.fineReversals<Settings.requiredReversals)
                {result.valid=false;result.reason="Insufficient reversals; no valid position estimate.";}
            }
            else
            {
                foreach(var response in responses){result.meanPosition+=response.positionInTrackingSpace;result.coordinateMetres+=response.coordinateMetres;}
                result.meanPosition/=responses.Count;result.coordinateMetres/=responses.Count;
                foreach(var response in responses)result.sdMetres+=Mathf.Pow(response.coordinateMetres-result.coordinateMetres,2);
                result.sdMetres=responses.Count>1?Mathf.Sqrt(result.sdMetres/(responses.Count-1)):0;
            }
            if(responses.Exists(response=>response.boundary)){result.valid=false;result.reason="Measurement reached its range limit; expand the range before a new trial.";}
            return result;
        }
        float LastMean(List<float> values)
        {if(values.Count==0)return 0;int start=Math.Max(0,values.Count-Settings.requiredReversals);float sum=0;for(int i=start;i<values.Count;i++)sum+=values[i];return sum/(values.Count-start);}
        public static float Drift(DriftEstimate before,DriftEstimate after)
        {if(!before.valid||!after.valid||before.method!=after.method)throw new InvalidOperationException("Drift needs valid pre/post estimates from the same method.");return after.coordinateMetres-before.coordinateMetres;}
    }
}
