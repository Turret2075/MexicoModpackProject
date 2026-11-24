using System.Collections;
using System.Collections.Generic;
using Games.Rebuilt.Components;
using UnityEngine;

namespace Prefabs.Games.Rebuilt.Robots.Mods.ChinaMod.IronPulse
{
    [CreateAssetMenu(fileName = "Interpolation Set", menuName = "Robot/Interpolation Set")]
    public class Interpolation : ScriptableObject
    {
        [Header("AZ Interp")]
        [SerializeField] public RebuiltInterpolation AZHoodInterp;
        [SerializeField] public RebuiltInterpolation AZFlywheelInterp;

        [Header("NZ Interp")]
        [SerializeField] public RebuiltInterpolation NZHoodInterp;
        [SerializeField] public RebuiltInterpolation NZFlywheelInterp;

        public void SetTransformOverride(Transform transform)
        {
            AZHoodInterp.transformOverride = transform;
            AZFlywheelInterp.transformOverride = transform;
            NZHoodInterp.transformOverride = transform;
            NZFlywheelInterp.transformOverride = transform;
        }

        public void ForceAZInterp()
        {
            NZHoodInterp = AZHoodInterp;
            NZFlywheelInterp = AZFlywheelInterp;
        }
    }
}
