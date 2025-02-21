﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Xv2CoreLib.Resource.UndoRedo;

namespace Xv2CoreLib.EMP_NEW.Keyframes
{
    [Serializable]
    public abstract class KeyframedBaseValue : INotifyPropertyChanged
    {
        #region NotifyPropChanged
        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyPropertyChanged(String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        private bool _isAnimated = false;
        private bool _loop = false;
        private bool _interpolate = true;

        public bool IsAnimated
        {
            get => _isAnimated;
            set
            {
                if(value != _isAnimated)
                {
                    _isAnimated = value;
                    NotifyPropertyChanged(nameof(IsAnimated));
                }
            }
        }
        public bool UndoableIsAnimated
        {
            get => IsAnimated;
            set
            {
                if(IsAnimated != value)
                {
                    UndoManager.Instance.AddUndo(new UndoablePropertyGeneric(nameof(IsAnimated), this, IsAnimated, value, value ? $"{ValueType} animation enabled" : $"{ValueType} animation disabled"));
                    IsAnimated = value;
                    NotifyPropertyChanged(nameof(UndoableIsAnimated));
                }
            }
        }
        public bool Loop
        {
            get => _loop;
            set
            {
                if(_loop != value)
                {
                    _loop = value;
                    NotifyPropertyChanged(nameof(Loop));
                }
            }
        }
        public bool Interpolate
        {
            get => _interpolate;
            set
            {
                if (_interpolate != value)
                {
                    _interpolate = value;
                    NotifyPropertyChanged(nameof(Interpolate));
                }
            }
        }
        public virtual KeyframedValueType ValueType { get; protected set; }
        protected byte Parameter { get; set; }
        public byte[] Components { get; set; }
        public ETR.ETR_InterpolationType ETR_InterpolationType { get; set; }

        public bool IsEtrValue { get; protected set; }
        public bool IsModifierValue { get; protected set; }

        /// <summary>
        /// Decompiles an array of <see cref="EMP_KeyframedValue"/> into an array of keyframes with synchronized timings. 
        /// </summary>
        protected List<KeyframedGenericValue>[] Decompile(float[] constant, params EMP_KeyframedValue[] keyframeValues)
        {
            //Interpolation setting should be shared between all EMP_KeyframedValues for the same parameter/value (and it is the case in all vanilla EMP files for XV2, SDBH and Breakers)
            Interpolate = keyframeValues[0].Interpolate;
            ETR_InterpolationType = keyframeValues[0].ETR_InterpolationType;
            IsAnimated = false;

            List<KeyframedGenericValue>[] values = new List<KeyframedGenericValue>[keyframeValues.Length];

            //Add all keyframes from EMP
            for(int i = 0; i < keyframeValues.Length; i++)
            {
                if (!keyframeValues[i].IsDefault)
                {
                    IsAnimated = true;
                }

                values[i] = new List<KeyframedGenericValue>();

                if(keyframeValues[i].Keyframes.Count == 0 || keyframeValues[i].IsDefault)
                {
                    //In this case, the value isn't animated and so the constant should be used
                    values[i].Add(new KeyframedGenericValue(0, constant[i]));
                }
                else
                {
                    foreach (var keyframe in keyframeValues[i].Keyframes)
                    {
                        int idx = values[i].IndexOf(values[i].FirstOrDefault(x => x.Time == keyframe.Time));

                        if(idx == -1)
                        {
                            values[i].Add(new KeyframedGenericValue(keyframe.Time, keyframe.Value));
                        }
                        else
                        {
                            //For whatever MORONIC reason... some EMPs have multiple keyframes at the same time. 
                            //We will just be keeping the last keyframe of these duplicates, so just overwrite the previous here
                            values[i][idx] = new KeyframedGenericValue(keyframe.Time, keyframe.Value);
                        }
                    }
                }
            }

            //Handle loops
            Loop = false;

            if(keyframeValues.All(x => x.Loop))
            {
                Loop = true;
            }
            else if (keyframeValues.Any(x => x.Loop))
            {
                //At least one set of keyframes have loop enabled, but not all do
                //The way we're decompiling the keyframes doesn't allow mixed-loops like this, so we must manually add looped keyframes to fill out the duration.
                //The duration must be a minimum of 101 (the particles whole life time). This also means that non-looped values will need to be extended as well, if they are less than that.
                int newDuration = keyframeValues.Max(x => x.Duration) > 101 ? keyframeValues.Max(x => x.Duration) : 101;

                if (newDuration == 0)
                    newDuration = 101;

                for(int i = 0; i < values.Length; i++)
                {
                    if (!keyframeValues[i].Loop) continue;

                    int loopDuration = keyframeValues[i].Duration;
                    int currentDuration = loopDuration;

                    if(loopDuration != 0)
                    {
                        while (currentDuration < newDuration)
                        {
                            for (int a = 0; a < loopDuration; a++)
                            {
                                if (currentDuration + a >= newDuration) break;

                                values[i].Add(new KeyframedGenericValue((ushort)(currentDuration + a), EMP_Keyframe.GetInterpolatedKeyframe(values[i], a, keyframeValues[i].Interpolate)));
                            }

                            currentDuration += loopDuration;
                        }
                    }
                    else
                    {
                        //Special case: looped keyframe with duration of 0. 
                        values[i].Add(new KeyframedGenericValue((ushort)(newDuration - 1), values[i][0].Value));
                    }

                }
            }

            //Add interpolated values for missing keyframes, or if interpolation is disabled, add in the last actual keyframe
            //This aligns all keyframes list together, ensuring they have keyframes at the same timings
            foreach(var keyframe in KeyframedGenericValue.AllKeyframeTimes(values))
            {
                for(int i = 0; i < values.Length; i++)
                {
                    //Check if keyframe exists at this time in the current values
                    if (values[i].FirstOrDefault(x => x.Time == keyframe) == null)
                    {
                        values[i].Add(new KeyframedGenericValue(keyframe, EMP_Keyframe.GetInterpolatedKeyframe(values[i], keyframe, Interpolate)));
                    }
                }
            }

            //Final sorting pass
            for(int i = 0; i < values.Length; i++)
            {
                values[i].Sort((x, y) => x.Time - y.Time);
            }

            if(values.Any(x => x.Count != values[0].Count))
            {
                throw new Exception("KeyframedBaseValue.Decompile: Decompiled keyframes are out of sync.");
            }

            return values;
        }

        /// <summary>
        /// Compiles the keyframes back to an array of <see cref="EMP_KeyframedValue"/>
        /// </summary>
        protected EMP_KeyframedValue[] Compile(float[] constant, List<KeyframedGenericValue>[] keyframes)
        {
            EMP_KeyframedValue[] empKeyframes = new EMP_KeyframedValue[keyframes.Length];

            if (IsAnimated)
            {
                for (int i = 0; i < keyframes.Length; i++)
                {
                    if (keyframes[i].Any(x => x.Value != constant[i]) || IsModifierValue)
                    {
                        empKeyframes[i] = new EMP_KeyframedValue();
                        empKeyframes[i].Loop = Loop;
                        empKeyframes[i].Interpolate = Interpolate;
                        empKeyframes[i].Parameter = Parameter;
                        empKeyframes[i].Component = Components[i];
                        empKeyframes[i].DefaultValue = IsModifierValue ? constant[i] : 0f;
                        empKeyframes[i].ETR_InterpolationType = ETR_InterpolationType;

                        for (int a = 0; a < keyframes[i].Count; a++)
                        {
                            empKeyframes[i].Keyframes.Add(new EMP_Keyframe()
                            {
                                Time = keyframes[i][a].Time,
                                Value = keyframes[i][a].Value
                            });
                        }
                    }
                }
            }
            else if (IsModifierValue)
            {
                //Even if a modifier isn't animated it will still need a definition to hold the constant values
                for (int i = 0; i < keyframes.Length; i++)
                {
                    empKeyframes[i] = new EMP_KeyframedValue();
                    empKeyframes[i].Parameter = Parameter;
                    empKeyframes[i].Component = Components[i];
                    empKeyframes[i].DefaultValue = constant[i];
                    empKeyframes[i].ETR_InterpolationType = ETR_InterpolationType;
                    empKeyframes[i].Keyframes.Add(new EMP_Keyframe(0, constant[i]));
                }
            }

            return empKeyframes;
        }

        protected void SetParameterAndComponents(bool isSphere = false, bool isScaleXyEnabled = false)
        {
            Parameter = EMP_KeyframedValue.GetParameter(ValueType, isSphere);
            Components = EMP_KeyframedValue.GetComponent(ValueType, isScaleXyEnabled);
        }

        public string GetValueName()
        {
            switch (ValueType)
            {
                case KeyframedValueType.ActiveRotation:
                    return "Active Rotation";
                case KeyframedValueType.Color1:
                case KeyframedValueType.ETR_Color1:
                    return "Color (Primary)";
                case KeyframedValueType.Color2:
                case KeyframedValueType.ETR_Color2:
                    return "Color (Secondary)";
                case KeyframedValueType.Color1_Transparency:
                case KeyframedValueType.ETR_Color1_Transparency:
                    return "Alpha (Primary)";
                case KeyframedValueType.Color2_Transparency:
                case KeyframedValueType.ETR_Color2_Transparency:
                    return "Alpha (Secondary)";
                case KeyframedValueType.PositionY:
                    return "Position Y";
                case KeyframedValueType.ScaleBase:
                case KeyframedValueType.ETR_Scale:
                    return "Scale";
                case KeyframedValueType.ScaleXY:
                    return "Scale XY";
                case KeyframedValueType.Size1:
                    return "Size 1";
                case KeyframedValueType.Size2:
                    return "Size 2";
                case KeyframedValueType.ECF_AmbientColor:
                    return "Add Color";
                case KeyframedValueType.ECF_MultiColor:
                    return "Multiplier";
                case KeyframedValueType.ECF_RimColor:
                    return "Rim Color";
                case KeyframedValueType.ECF_AmbientTransparency:
                    return "Add Factor";
                case KeyframedValueType.ECF_DiffuseTransparency:
                    return "Multi Factor";
                case KeyframedValueType.ECF_SpecularTransparency:
                    return "Rim Factor";
                case KeyframedValueType.ECF_BlendingFactor:
                    return "Blending Factor";
                case KeyframedValueType.Modifier_Axis:
                case KeyframedValueType.Modifier_Axis2:
                    return "Axis";
                case KeyframedValueType.Modifier_RotationRate:
                    return "Rotation Rate";
                case KeyframedValueType.Modifier_Radial:
                    return "Radial";
                case KeyframedValueType.Modifier_DragStrength:
                    return "Drag Strength";
                case KeyframedValueType.Modifier_Direction:
                    return "Direction";
                default:
                    return ValueType.ToString();
            }
        }

    }

    public class KeyframedGenericValue : IKeyframe
    {
        public ushort Time { get; set; }
        public float Value { get; set; }

        public KeyframedGenericValue(ushort time, float value)
        {
            Time = time;
            Value = value;
        }

        public override string ToString()
        {
            return $"{Time}: {Value}";
        }

        public static IEnumerable<ushort> AllKeyframeTimes(IList<KeyframedGenericValue>[] values)
        {
            if (values.Length == 0) yield break;
            List<int> current = new List<int>(values[0].Count);

            for(int i = 0; i < values.Length; i++)
            {
                for(int a = 0; a < values[i].Count; a++)
                {
                    if (current.Contains(values[i][a].Time)) continue;
                    current.Add(values[i][a].Time);
                    yield return values[i][a].Time;
                }
            }
        }
    }

    [Serializable]
    public abstract class KeyframeBaseValue : INotifyPropertyChanged
    {
        #region NotifyPropChanged
        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private float _time = 0f;
        public float Time
        {
            get => _time;
            set
            {
                _time = value;
                NotifyPropertyChanged(nameof(Time));
                NotifyPropertyChanged(nameof(UndoableTime));
            }
        }
        public float UndoableTime
        {
            get => _time;
            set
            {
                if(_time != value)
                {
                    UndoManager.Instance.AddUndo(new UndoablePropertyGeneric(nameof(Time), this, Time, value, "Keyframe Time"));
                    Time = value;
                }
            }
        }
    }

    public interface ISelectedKeyframedValue
    {
        KeyframedBaseValue SelectedKeyframedValue { get; set; }
    }
}
