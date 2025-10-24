//using System.Diagnostics;

using System.Diagnostics;
using System.Threading;
//using Leap;
using T3.Core.Utils;
using LeapVector = Leap.Vector;

[Guid("e0006880-d593-499a-82df-6b68ce7f859e")]
internal sealed class LeapData : Instance<LeapData>
{
    [Output(Guid = "F66202A7-1215-4021-886D-671645E93650")]
    public readonly Slot<Vector3> Position = new();

    [Output(Guid = "8DFEFC2A-C80A-4D81-9C8C-8D24EFACA240")]
    public readonly Slot<Vector3> RotationAngles = new();

    [Output(Guid = "E5176A0A-547D-45A0-BF1F-49A87351465E")]
    public readonly Slot<Vector3> Parameter = new();

    [Output(Guid = "958A76D2-5E8D-47D2-8D24-B79D881305AF")]
    public readonly Slot<float> Quality = new();

    public LeapData()
    {
        Position.UpdateAction += Update;

        _controller = new Leap.Controller();
        _leapHandler = new LeapHandler();
        
        _handlerConnected = _controller.AddListener(_leapHandler);
        if (!_handlerConnected)
            Log.Error("Error connecting to leap controller", this);
    }

    private void Update(EvaluationContext context)
    {
        if (!_controller.IsConnected || !_handlerConnected)
        {
            if (!_warningPrintedOnce)
            {
                Log.Error("leap device not found or not connected", this);
                _warningPrintedOnce = true;
            }

            return;
        }

        if (_warningPrintedOnce)
        {
            Log.Debug("Leap device connected", this);
            _warningPrintedOnce = false;
        }

        var running = Enable.GetValue(context);

        if (running && !_leapHandler.IsRunning)
        {
            _leapHandler.Start();
        }

        if (!running && _leapHandler.IsRunning)
        {
            _leapHandler.Stop();
        }

        _leapHandler.WorkspaceCenter = ToLeapVec3(WorkspaceCenter.GetValue(context));
        _leapHandler.WorkspaceRadius = WorkspaceRadius.GetValue(context);
        
        _leapHandler.TrackingRange = 20;

        _leapHandler.CameraFriction = CameraFriction.GetValue(context);
        _leapHandler.ParameterFriction = ParameterFriction.GetValue(context);

        Position.Value = _leapHandler.Position;

        var paramCenter = ParameterCenter.GetValue(context);
        _leapHandler.ParameterCenter = ToLeapVec3(paramCenter);
        
        var paramRange = ParameterRange.GetValue(context);
        Parameter.Value
            = new Vector3(
                          MapInput(_leapHandler.Parameter.X, -1, 0, 1, paramCenter.X - paramRange.X, paramCenter.X, paramCenter.X + paramRange.X),
                          MapInput(_leapHandler.Parameter.Y, -1, 0, 1, paramCenter.Y - paramRange.Y, paramCenter.Y, paramCenter.Y + paramRange.Y),
                          MapInput(_leapHandler.Parameter.Z, -1, 0, 1, paramCenter.Z + paramRange.Z, paramCenter.Z, paramCenter.Z - paramRange.Z)
                         );

        RotationAngles.Value = new Vector3(_leapHandler.Rotation.Y, _leapHandler.Rotation.X, _leapHandler.Rotation.Z); // Careful: Rotation Order 
        Quality.Value = _leapHandler.Quality;

    }

    [Input(Guid = "7DC4EC0F-A301-4A5B-8E3F-B63422D4958F")]
    public readonly InputSlot<bool> Enable = new();

    [Input(Guid = "3B08AC0E-A563-49E0-909C-61A8926113B2")]
    public readonly InputSlot<Vector3> WorkspaceCenter = new();

    [Input(Guid = "F9D5A645-AF93-4C41-9EC8-52C6913011C6")]
    public readonly InputSlot<float> WorkspaceRadius = new();

    [Input(Guid = "D6571C23-EF6B-4FAE-B5D5-F2F02457933B")]
    public readonly InputSlot<Vector3> ParameterCenter = new();

    [Input(Guid = "F49C1A33-CBA9-4E11-A5CB-892954E83E65")]
    public readonly InputSlot<Vector3> ParameterRange = new();

    [Input(Guid = "EA1D6739-5917-4158-938D-D67EB8DC53FE")]
    public readonly InputSlot<float> CameraFriction = new();

    [Input(Guid = "61D968AA-E7DA-4C89-A34B-4604FC171DB6")]
    public readonly InputSlot<float> ParameterFriction = new();

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _controller.RemoveListener(_leapHandler);
        Utilities.Dispose(ref _controller);
        Utilities.Dispose(ref _leapHandler);
        _handlerConnected = false;
    }

    private static float MapInput(float input, float minValue, float neutralValue, float maxValue, float minPoint, float neutralPoint, float maxPoint,
                                  float neutralWidth = 0)
    {
        float output;
        if (neutralWidth < 0.001)
        {
            var normalizedInput = (input - minPoint) / (maxPoint - minPoint);
            normalizedInput = normalizedInput.Clamp(0.0f, 1.0f);
            output = MathUtils.SmootherStep(0, 1, normalizedInput) * (maxValue - minValue) + minValue;
        }
        else
        {
            if (minPoint > maxPoint)
            {
                Utilities.Swap(ref minPoint, ref maxPoint);
                Utilities.Swap(ref minValue, ref maxValue);
            }

            if (input < 0)
            {
                var normalizedInput = (input + neutralPoint - minPoint) / (neutralPoint - neutralWidth * 0.5f - minPoint);
                normalizedInput = normalizedInput.Clamp(0.0f, 1.0f);
                output = MathUtils.SmootherStep(0, 1, normalizedInput) * (neutralValue - minValue) + minValue;
            }
            else
            {
                var normalizedInput = (input + neutralPoint - neutralPoint - neutralWidth * 0.5f) / (maxPoint - neutralPoint - neutralWidth * 0.5f);
                normalizedInput = normalizedInput.Clamp(0.0f, 1.0f);
                output = MathUtils.SmootherStep(0, 1, normalizedInput) * (maxValue - neutralValue) + neutralValue;
            }
        }

        return output;
    }

    private Leap.Controller _controller;
    private bool _handlerConnected;
    private LeapHandler _leapHandler;
    private bool _warningPrintedOnce;

    private static LeapVector ToLeapVec3(Vector3 vec3)
    {
        return new LeapVector(vec3.X, vec3.Y, vec3.Z);
    }

    private sealed class LeapHandler : Leap.Listener
    {
        private bool IsInitialized { get; set; }
        public bool IsRunning { get; private set; }

        public float MaxGapTime { get; set; }

        public LeapVector WorkspaceCenter { get; set; }
        public float WorkspaceRadius { get; set; }
        public LeapVector ParameterCenter { get; set; }
        public float TrackingRange { get; set; }

        public float CameraFriction { get; set; }
        public float ParameterFriction { get; set; }

        public int Reference { get; set; }

        public Vector3 Position
        {
            get
            {
                if (!IsRunning)
                    return Vector3.Zero;

                LeapVector v;
                lock (_thisLock)
                {
                    v = _smoothedPosition - WorkspaceCenter;
                }

                return new Vector3(v.x, v.y, v.z);
            }
        }

        public Vector3 Rotation // yaw, pitch, roll in degree
        {
            get
            {
                if (!IsRunning)
                    return Vector3.Zero;

                LeapVector v;
                lock (_thisLock)
                {
                    v = _smoothedRotation;
                }

                return new Vector3(v.x, v.y, v.z);
            }
        }

        public Vector3 Parameter
        {
            get
            {
                if (!IsRunning)
                    return Vector3.Zero;

                LeapVector v;
                lock (_thisLock)
                {
                    v = _smoothedParameter;
                }

                return new Vector3(v.x, v.y, v.z);
            }
        }

        public float Quality
        {
            get
            {
                if (!IsRunning)
                    return 0.0f;

                float quality = 0;
                lock (_thisLock)
                {
                    var fadeOutTime = 1.0f;
                    quality = (float)_timeFromParameterPointableBecomesInvisible.ElapsedTicks / Stopwatch.Frequency < fadeOutTime ? 1.0f : 0.0f;
                }

                return quality;
            }
        }

        public LeapHandler()
        {
            MaxGapTime = 1.0f;
            WorkspaceCenter = new LeapVector(0, 250, 0);
            WorkspaceRadius = 250;
            ParameterCenter = new LeapVector(-50, 200, 0);
            TrackingRange = 50;
            _timeFromCameraPointableBecomesInvisible.Restart();
            _timeFromParameterPointableBecomesInvisible.Restart();
        }

        public void Start()
        {
            if (!IsInitialized)
                return;
            
            IsRunning = true;
            _currentPosition = Leap.Vector.Zero;
            _lastPosition = _currentPosition;

            _currentRotation = Leap.Vector.Zero;
            _lastRotation = _currentRotation;

            _currentParameter = Leap.Vector.Zero;
            _lastParameter = _currentParameter;

            lock (_thisLock)
            {
                _smoothedPosition = _currentPosition;
                _smoothedRotation = _currentRotation;
                _smoothedParameter = _currentParameter;
            }

            _timeFromCameraPointableBecomesInvisible.Restart();
            _timeFromParameterPointableBecomesInvisible.Restart();
            _frameTimer.Start();
            _lastUsedCameraPointableId = -1;
            _lastUsedParameterPointableId = -1;
            _cameraFriction = 0.9f;
            _parameterFriction = 0.9f;
            _smoothedCameraFriction = _cameraFriction;
            _smoothedParameterFriction = _parameterFriction;
        }

        public void Stop()
        {
            IsRunning = false;
            _timeFromCameraPointableBecomesInvisible.Reset();
            _timeFromParameterPointableBecomesInvisible.Reset();
            _frameTimer.Stop();
        }

        public override void OnInit(Leap.Controller controller)
        {
            IsRunning = false;
            IsInitialized = false;
        }

        public override void OnConnect(Leap.Controller controller)
        {
            IsInitialized = true;
            Log.Debug("LEAP: On connect");
        }

        public override void OnDisconnect(Leap.Controller controller)
        {
            IsInitialized = false;
            IsRunning = false;
            Log.Debug("LEAP: On disconnect");
        }

        public override void OnExit(Leap.Controller controller)
        {
            IsInitialized = false;
            IsRunning = false;
        }

        public override void OnFrame(Leap.Controller controller)
        {
            Log.Debug("LEAP: On Frame");
            if (!IsRunning)
                return;
            

            //var maxTipVelocityForNewPointables = 40.0f;
            var minDistanceBetweenNewPointableToCameraAndParameterPointables = 150.0f;
            var fadeOutTime = 0.2f;

            var cameraPointable = Leap.Pointable.Invalid;
            var parameterPointable = Leap.Pointable.Invalid;

            using (var frame = controller.Frame())
            {
                if (frame.IsValid)
                {
                    cameraPointable = frame.Pointable(_lastUsedCameraPointableId);
                    parameterPointable = frame.Pointable(_lastUsedParameterPointableId);

                    if (!cameraPointable.IsValid)
                    {
                        float minZ = 99999;
                        var frontMostPointable = Leap.Pointable.Invalid;
                        float minD = 99999;
                        var closestPointableToLastCameraPosition = Leap.Pointable.Invalid;
                        foreach (var p in frame.Pointables)
                        {
                            var pointableEqualsParameterPointable = p.Equals(parameterPointable);
                            var pointableIsOnParameterHand = p.Hand.Equals(parameterPointable.Hand);
                            if (p.IsValid && !pointableEqualsParameterPointable && !pointableIsOnParameterHand)
                            {
                                var isInWorkspace = (WorkspaceCenter - p.TipPosition).Magnitude < WorkspaceRadius;
                                var pointableIsNearParameterPointable = (p.TipPosition - parameterPointable.TipPosition).Magnitude <
                                                                        minDistanceBetweenNewPointableToCameraAndParameterPointables;

                                if (isInWorkspace && p.TipPosition.z < minZ && !pointableIsNearParameterPointable)
                                {
                                    minZ = p.TipPosition.z;
                                    frontMostPointable = p;
                                }

                                var pos = p.TipPosition - p.Direction * 90.0f;
                                var distanceToLastPosition = (_currentPosition - (pos - WorkspaceCenter)).Magnitude;
                                if (isInWorkspace && distanceToLastPosition < minD && !pointableIsNearParameterPointable)
                                {
                                    minD = distanceToLastPosition;
                                    closestPointableToLastCameraPosition = p;
                                }
                            }
                        }

                        if (closestPointableToLastCameraPosition.IsValid && minD < TrackingRange)
                        {
                            cameraPointable = closestPointableToLastCameraPosition;
                            Log.Debug("get new camera pointable near to last known pointable: {0}", cameraPointable.Id);
                        }
                        else if (frontMostPointable.IsValid)
                        {
                            cameraPointable = frontMostPointable;
                            Log.Debug("get new camera pointable from front most pointable: {0}", cameraPointable.Id);
                        }
                    }

                    if (!parameterPointable.IsValid && cameraPointable.IsValid)
                    {
                        float maxZ = -99999;
                        var backMostPointable = Leap.Pointable.Invalid;
                        float minD = 99999;
                        var closestPointableToLastParameterPosition = Leap.Pointable.Invalid;
                        foreach (var p in frame.Pointables)
                        {
                            var pointableEqualsCameraPointable = p.Equals(cameraPointable);
                            var pointableIsOnCameraHand = p.Hand.Equals(cameraPointable.Hand);
                            if (p.IsValid && !pointableEqualsCameraPointable && !pointableIsOnCameraHand)
                            {
                                var isInWorkspace = (WorkspaceCenter - p.TipPosition).Magnitude < WorkspaceRadius;
                                var pointableIsNearCameraPointable = (p.TipPosition - cameraPointable.TipPosition).Magnitude <
                                                                     minDistanceBetweenNewPointableToCameraAndParameterPointables;

                                if (isInWorkspace && p.TipPosition.z > maxZ && !pointableIsNearCameraPointable)
                                {
                                    maxZ = p.TipPosition.z;
                                    backMostPointable = p;
                                }

                                var distanceToLastPosition = (_currentParameter + ParameterCenter - p.TipPosition).Magnitude;
                                if (isInWorkspace && distanceToLastPosition < minD && !pointableIsNearCameraPointable)
                                {
                                    minD = distanceToLastPosition;
                                    closestPointableToLastParameterPosition = p;
                                }
                            }
                        }

                        if (closestPointableToLastParameterPosition.IsValid && minD < TrackingRange)
                        {
                            parameterPointable = closestPointableToLastParameterPosition;
                            Log.Debug("get new parameter pointable near to last known pointable: {0}", parameterPointable.Id);
                        }
                        else if (backMostPointable.IsValid)
                        {
                            parameterPointable = backMostPointable;
                            Log.Debug("get new parameter pointable from back most pointable: {0}", parameterPointable.Id);
                        }
                    }
                }
            }

            var cameraPointableDetectedInFrame = cameraPointable.IsValid;
            var cameraPointableDetectionTriggerDownFlank = !cameraPointableDetectedInFrame && _cameraPointableDetectedInLastFrame;
            _cameraPointableDetectedInLastFrame = cameraPointableDetectedInFrame;

            var parameterPointableDetectedInFrame = parameterPointable.IsValid;
            var parameterPointableDetectionTriggerDownFlank = !parameterPointableDetectedInFrame && _parameterPointableDetectedInLastFrame;
            _parameterPointableDetectedInLastFrame = parameterPointableDetectedInFrame;

            if (cameraPointableDetectionTriggerDownFlank)
            {
                _timeFromCameraPointableBecomesInvisible.Restart();
            }

            if (parameterPointableDetectionTriggerDownFlank)
            {
                _timeFromParameterPointableBecomesInvisible.Restart();
            }

            if (cameraPointableDetectedInFrame)
            {
                using (cameraPointable)
                {
                    var pos = cameraPointable.TipPosition - cameraPointable.Direction * 90.0f;
                    _currentPosition = pos - WorkspaceCenter;
                    _currentRotation = new Leap.Vector(cameraPointable.Direction.Yaw * 180.0f / (float)Math.PI,
                                                       -cameraPointable.Direction.Pitch * 180.0f / (float)Math.PI,
                                                       0);
                    _lastUsedCameraPointableId = cameraPointable.Id;
                    _timeFromCameraPointableBecomesInvisible.Reset();
                }
            }
            else if (_lastUsedCameraPointableId >= 0 && (double)_timeFromCameraPointableBecomesInvisible.ElapsedTicks / Stopwatch.Frequency > fadeOutTime)
            {
                Log.Debug("camera pointable lost: {0}", _lastUsedCameraPointableId);
                _currentPosition = Leap.Vector.Zero;
                _currentRotation = Leap.Vector.Zero;
                _lastUsedCameraPointableId = -1;
            }

            if (parameterPointableDetectedInFrame)
            {
                using (parameterPointable)
                {
                    _currentParameter = parameterPointable.TipPosition - ParameterCenter;
                    _timeFromParameterPointableBecomesInvisible.Reset();
                    _lastUsedParameterPointableId = parameterPointable.Id;
                }
            }
            else if (_lastUsedParameterPointableId >= 0 && (double)_timeFromParameterPointableBecomesInvisible.ElapsedTicks / Stopwatch.Frequency > fadeOutTime)
            {
                Log.Debug("parameter pointable lost: {0}", _lastUsedParameterPointableId);
                _currentParameter = Leap.Vector.Zero;
                _lastUsedParameterPointableId = -1;
            }

            var elapsed = (float)((double)_frameTimer.ElapsedTicks / Stopwatch.Frequency);
            _frameTimer.Restart();

            if ((_lastPosition - _currentPosition).Magnitude > 100.0f)
            {
                _smoothedCameraFriction = 0.9999f;
            }

            if ((_lastParameter - _currentParameter).Magnitude > 100.0f)
            {
                _smoothedParameterFriction = 0.9999f;
            }

            var frictionFriction = 0.99f;
            _smoothedCameraFriction = frictionFriction * _smoothedCameraFriction + (1.0f - frictionFriction) * _cameraFriction;
            _smoothedParameterFriction = frictionFriction * _smoothedParameterFriction + (1.0f - frictionFriction) * _parameterFriction;

            lock (_thisLock)
            {
                _smoothedPosition = _smoothedCameraFriction * _smoothedPosition + (1.0f - _smoothedCameraFriction) * (WorkspaceCenter + _currentPosition);
                _smoothedRotation = _smoothedCameraFriction * _smoothedRotation + (1.0f - _smoothedCameraFriction) * _currentRotation;
                _smoothedParameter = _smoothedParameterFriction * _smoothedParameter +
                                     (1.0f - _smoothedParameterFriction) * (ParameterCenter + _currentParameter);
            }

            _lastPosition = _currentPosition;
            _lastRotation = _currentRotation;
            _lastParameter = _currentParameter;
        }

        private bool _cameraPointableDetectedInLastFrame;
        private bool _parameterPointableDetectedInLastFrame;
        private readonly Stopwatch _timeFromCameraPointableBecomesInvisible = new();
        private readonly Stopwatch _timeFromParameterPointableBecomesInvisible = new();
        private readonly Stopwatch _frameTimer = new();

        private LeapVector _currentPosition = LeapVector.Zero; //relative to the workspace center coordinate system
        private LeapVector _lastPosition = LeapVector.Zero;
        private LeapVector _smoothedPosition = LeapVector.Zero;

        private LeapVector _currentRotation = LeapVector.Zero;
        private LeapVector _lastRotation = LeapVector.Zero;
        private LeapVector _smoothedRotation = LeapVector.Zero;

        private LeapVector _currentParameter = LeapVector.Zero; //relative to the parameter center coordinate system
        private LeapVector _lastParameter = LeapVector.Zero;
        private LeapVector _smoothedParameter = LeapVector.Zero;

        private float _cameraFriction;
        private float _parameterFriction;

        private float _smoothedCameraFriction;
        private float _smoothedParameterFriction;

        private int _lastUsedCameraPointableId = -1;
        private int _lastUsedParameterPointableId = -1;

        private readonly Lock _thisLock = new();
    }
}