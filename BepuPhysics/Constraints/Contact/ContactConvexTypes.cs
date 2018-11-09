﻿using BepuPhysics.CollisionDetection;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using BepuUtilities;
using BepuUtilities.Memory;
using static BepuUtilities.GatherScatter;
namespace BepuPhysics.Constraints.Contact
{  
    public struct Contact1 : IConvexTwoBodyContactConstraintDescription<Contact1>
    {
        public ConstraintContactData Contact0;
        public Vector3 OffsetB;
        public float FrictionCoefficient;
        public Vector3 Normal;
        public SpringSettings SpringSettings;
        public float MaximumRecoveryVelocity;

        public void ApplyDescription(ref TypeBatch batch, int bundleIndex, int innerIndex)
        {
		    Debug.Assert(batch.TypeId == ConstraintTypeId, "The type batch passed to the description must match the description's expected type.");
            ref var target = ref GetOffsetInstance(ref Buffer<Contact1PrestepData>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            Vector3Wide.WriteFirst(Contact0.OffsetA, ref target.OffsetA0);
            GetFirst(ref target.PenetrationDepth0) = Contact0.PenetrationDepth;
			
            Vector3Wide.WriteFirst(OffsetB, ref target.OffsetB);

            GetFirst(ref target.FrictionCoefficient) = FrictionCoefficient;
			
            Vector3Wide.WriteFirst(Normal, ref target.Normal);
			
            SpringSettingsWide.WriteFirst(SpringSettings, ref target.SpringSettings);
            GetFirst(ref target.MaximumRecoveryVelocity) = MaximumRecoveryVelocity;					
        }

        public void BuildDescription(ref TypeBatch batch, int bundleIndex, int innerIndex, out Contact1 description)
        {    
			Debug.Assert(batch.TypeId == ConstraintTypeId, "The type batch passed to the description must match the description's expected type.");
            ref var source = ref GetOffsetInstance(ref Buffer<Contact4PrestepData>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            Vector3Wide.ReadFirst(source.OffsetA0, out description.Contact0.OffsetA);
            description.Contact0.PenetrationDepth = GetFirst(ref source.PenetrationDepth0);
			
            Vector3Wide.ReadFirst(source.OffsetB, out description.OffsetB);

            description.FrictionCoefficient = GetFirst(ref source.FrictionCoefficient);
			
            Vector3Wide.ReadFirst(source.Normal, out description.Normal);
			
            SpringSettingsWide.ReadFirst(source.SpringSettings, out description.SpringSettings);
            description.MaximumRecoveryVelocity = GetFirst(ref source.MaximumRecoveryVelocity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyManifoldWideProperties(ref Vector3 offsetB, ref Vector3 normal, ref PairMaterialProperties material)
        {
            OffsetB = offsetB;
            FrictionCoefficient = material.FrictionCoefficient;
            Normal = normal;
            SpringSettings = material.SpringSettings;
            MaximumRecoveryVelocity = material.MaximumRecoveryVelocity;
        }
		
        public int ConstraintTypeId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Contact1TypeProcessor.BatchTypeId;
        }

        public Type BatchType => typeof(Contact1TypeProcessor);

    }

    public struct Contact1PrestepData
    {
        //NOTE: Prestep data memory layout is relied upon by the constraint description for marginally more efficient setting and getting.
        //If you modify this layout, be sure to update the associated ContactManifold4Constraint.
        //Note that this layout is defined by the execution order in the prestep. The function accesses it sequentially to ensure the prefetcher can do its job.
        public Vector3Wide OffsetA0;
        public Vector3Wide OffsetB;
        public Vector<float> FrictionCoefficient;
        //In a convex manifold, all contacts share the same normal and tangents.
        public Vector3Wide Normal;
        //All contacts also share the spring settings.
        public SpringSettingsWide SpringSettings;
        public Vector<float> MaximumRecoveryVelocity;
        public Vector<float> PenetrationDepth0;
    }

    public struct Contact1AccumulatedImpulses
    {
        public Vector2Wide Tangent;
        public Vector<float> Penetration0;
        public Vector<float> Twist;
    }

    public unsafe struct Contact1Projection
    {
        public BodyInertias InertiaA;
        public BodyInertias InertiaB;
        public Vector<float> PremultipliedFrictionCoefficient;
        public Vector3Wide Normal;
        public TangentFriction.Projection Tangent;
        public PenetrationLimit1.Projection Penetration;
        //Lever arms aren't included in the twist projection because the number of arms required varies independently of the twist projection itself.
        public Vector<float> LeverArm0;
        public TwistFrictionProjection Twist;
    }

	public struct Contact1Functions : IConstraintFunctions<Contact1PrestepData, Contact1Projection, Contact1AccumulatedImpulses>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Prestep(Bodies bodies, ref TwoBodyReferences bodyReferences, int count,
            float dt, float inverseDt, ref BodyInertias inertiaA, ref BodyInertias inertiaB, ref Contact1PrestepData prestep, out Contact1Projection projection)
        {
            //Be careful about the execution order here. It should be aligned with the prestep data layout to ensure prefetching works well.
            projection.InertiaA = inertiaA;
            projection.InertiaB = inertiaB;
            Vector3Wide.Subtract(prestep.OffsetA0, prestep.OffsetB, out var offsetToManifoldCenterB);
            projection.PremultipliedFrictionCoefficient = prestep.FrictionCoefficient;
            projection.Normal = prestep.Normal;
            Helpers.BuildOrthnormalBasis(ref prestep.Normal, out var x, out var z);
            TangentFriction.Prestep(ref x, ref z, ref prestep.OffsetA0, ref offsetToManifoldCenterB, ref projection.InertiaA, ref projection.InertiaB, out projection.Tangent);
            PenetrationLimit1.Prestep(ref projection.InertiaA, ref projection.InertiaB, ref prestep, dt, inverseDt, out projection.Penetration);
            //Just assume the lever arms for B are the same. It's a good guess. (The only reason we computed the offset B is because we didn't want to go into world space.)
            Vector3Wide.Distance(prestep.OffsetA0, prestep.OffsetA0, out projection.LeverArm0);
            TwistFriction.Prestep(ref projection.InertiaA, ref projection.InertiaB, ref prestep.Normal, out projection.Twist);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WarmStart(ref BodyVelocities wsvA, ref BodyVelocities wsvB, ref Contact1Projection projection, ref Contact1AccumulatedImpulses accumulatedImpulses)
        {
            Helpers.BuildOrthnormalBasis(ref projection.Normal, out var x, out var z);
            TangentFriction.WarmStart(ref x, ref z, ref projection.Tangent, ref projection.InertiaA, ref projection.InertiaB, ref accumulatedImpulses.Tangent, ref wsvA, ref wsvB);
            PenetrationLimit1.WarmStart(ref projection.Penetration, ref projection.InertiaA, ref projection.InertiaB,
                ref projection.Normal,
                ref accumulatedImpulses.Penetration0,
				ref wsvA, ref wsvB);
            TwistFriction.WarmStart(ref projection.Normal, ref projection.InertiaA, ref projection.InertiaB, ref accumulatedImpulses.Twist, ref wsvA, ref wsvB);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Solve(ref BodyVelocities wsvA, ref BodyVelocities wsvB, ref Contact1Projection projection, ref Contact1AccumulatedImpulses accumulatedImpulses)
        {
            Helpers.BuildOrthnormalBasis(ref projection.Normal, out var x, out var z);
            var maximumTangentImpulse = projection.PremultipliedFrictionCoefficient *
                (accumulatedImpulses.Penetration0);
            TangentFriction.Solve(ref x, ref z, ref projection.Tangent, ref projection.InertiaA, ref projection.InertiaB, ref maximumTangentImpulse, ref accumulatedImpulses.Tangent, ref wsvA, ref wsvB);
            //Note that we solve the penetration constraints after the friction constraints. 
            //This makes the penetration constraints more authoritative at the cost of the first iteration of the first frame of an impact lacking friction influence.
            //It's a pretty minor effect either way.
            PenetrationLimit1.Solve(ref projection.Penetration, ref projection.InertiaA, ref projection.InertiaB, ref projection.Normal,
                ref accumulatedImpulses.Penetration0,
				ref wsvA, ref wsvB);
            var maximumTwistImpulse = projection.PremultipliedFrictionCoefficient * (
                accumulatedImpulses.Penetration0 * projection.LeverArm0);
            TwistFriction.Solve(ref projection.Normal, ref projection.InertiaA, ref projection.InertiaB, ref projection.Twist, ref maximumTwistImpulse, ref accumulatedImpulses.Twist, ref wsvA, ref wsvB);
        }

    }
	
    /// <summary>
    /// Handles the solve iterations of a bunch of 1-contact two body manifold constraints.
    /// </summary>
    public class Contact1TypeProcessor :
        TwoBodyTypeProcessor<Contact1PrestepData, Contact1Projection, Contact1AccumulatedImpulses, Contact1Functions>
    {
        //Matches UpdateConstraintForManifold's manifoldTypeAsConstraintType computation.
        public const int BatchTypeId = 4;
    }


    public struct Contact2 : IConvexTwoBodyContactConstraintDescription<Contact2>
    {
        public ConstraintContactData Contact0;
        public ConstraintContactData Contact1;
        public Vector3 OffsetB;
        public float FrictionCoefficient;
        public Vector3 Normal;
        public SpringSettings SpringSettings;
        public float MaximumRecoveryVelocity;

        public void ApplyDescription(ref TypeBatch batch, int bundleIndex, int innerIndex)
        {
		    Debug.Assert(batch.TypeId == ConstraintTypeId, "The type batch passed to the description must match the description's expected type.");
            ref var target = ref GetOffsetInstance(ref Buffer<Contact2PrestepData>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            Vector3Wide.WriteFirst(Contact0.OffsetA, ref target.OffsetA0);
            GetFirst(ref target.PenetrationDepth0) = Contact0.PenetrationDepth;
            Vector3Wide.WriteFirst(Contact1.OffsetA, ref target.OffsetA1);
            GetFirst(ref target.PenetrationDepth1) = Contact1.PenetrationDepth;
			
            Vector3Wide.WriteFirst(OffsetB, ref target.OffsetB);

            GetFirst(ref target.FrictionCoefficient) = FrictionCoefficient;
			
            Vector3Wide.WriteFirst(Normal, ref target.Normal);
			
            SpringSettingsWide.WriteFirst(SpringSettings, ref target.SpringSettings);
            GetFirst(ref target.MaximumRecoveryVelocity) = MaximumRecoveryVelocity;					
        }

        public void BuildDescription(ref TypeBatch batch, int bundleIndex, int innerIndex, out Contact2 description)
        {    
			Debug.Assert(batch.TypeId == ConstraintTypeId, "The type batch passed to the description must match the description's expected type.");
            ref var source = ref GetOffsetInstance(ref Buffer<Contact4PrestepData>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            Vector3Wide.ReadFirst(source.OffsetA0, out description.Contact0.OffsetA);
            description.Contact0.PenetrationDepth = GetFirst(ref source.PenetrationDepth0);
            Vector3Wide.ReadFirst(source.OffsetA1, out description.Contact1.OffsetA);
            description.Contact1.PenetrationDepth = GetFirst(ref source.PenetrationDepth1);
			
            Vector3Wide.ReadFirst(source.OffsetB, out description.OffsetB);

            description.FrictionCoefficient = GetFirst(ref source.FrictionCoefficient);
			
            Vector3Wide.ReadFirst(source.Normal, out description.Normal);
			
            SpringSettingsWide.ReadFirst(source.SpringSettings, out description.SpringSettings);
            description.MaximumRecoveryVelocity = GetFirst(ref source.MaximumRecoveryVelocity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyManifoldWideProperties(ref Vector3 offsetB, ref Vector3 normal, ref PairMaterialProperties material)
        {
            OffsetB = offsetB;
            FrictionCoefficient = material.FrictionCoefficient;
            Normal = normal;
            SpringSettings = material.SpringSettings;
            MaximumRecoveryVelocity = material.MaximumRecoveryVelocity;
        }
		
        public int ConstraintTypeId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Contact2TypeProcessor.BatchTypeId;
        }

        public Type BatchType => typeof(Contact2TypeProcessor);

    }

    public struct Contact2PrestepData
    {
        //NOTE: Prestep data memory layout is relied upon by the constraint description for marginally more efficient setting and getting.
        //If you modify this layout, be sure to update the associated ContactManifold4Constraint.
        //Note that this layout is defined by the execution order in the prestep. The function accesses it sequentially to ensure the prefetcher can do its job.
        public Vector3Wide OffsetA0;
        public Vector3Wide OffsetA1;
        public Vector3Wide OffsetB;
        public Vector<float> FrictionCoefficient;
        //In a convex manifold, all contacts share the same normal and tangents.
        public Vector3Wide Normal;
        //All contacts also share the spring settings.
        public SpringSettingsWide SpringSettings;
        public Vector<float> MaximumRecoveryVelocity;
        public Vector<float> PenetrationDepth0;
        public Vector<float> PenetrationDepth1;
    }

    public struct Contact2AccumulatedImpulses
    {
        public Vector2Wide Tangent;
        public Vector<float> Penetration0;
        public Vector<float> Penetration1;
        public Vector<float> Twist;
    }

    public unsafe struct Contact2Projection
    {
        public BodyInertias InertiaA;
        public BodyInertias InertiaB;
        public Vector<float> PremultipliedFrictionCoefficient;
        public Vector3Wide Normal;
        public TangentFriction.Projection Tangent;
        public PenetrationLimit2.Projection Penetration;
        //Lever arms aren't included in the twist projection because the number of arms required varies independently of the twist projection itself.
        public Vector<float> LeverArm0;
        public Vector<float> LeverArm1;
        public TwistFrictionProjection Twist;
    }

	public struct Contact2Functions : IConstraintFunctions<Contact2PrestepData, Contact2Projection, Contact2AccumulatedImpulses>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComputeFrictionCenter(
			in Vector3Wide offsetA0, in Vector3Wide offsetA1, 
            in Vector<float> depth0,in Vector<float> depth1, out Vector3Wide center)
        {
            //This can sometimes cause a weird center of friction. That's a bit strange, but the alternative is often stranger:
            //Without this, if one contact is active and the other is speculative, friction will use the manifold center as halfway between the two points. If something is holding 
            //the inactive contact side up and swinging it around, the existence of speculative contacts would make friction work against the free swinging.
            var weight0 = Vector.ConditionalSelect(Vector.LessThan(depth0, Vector<float>.Zero), Vector<float>.Zero, Vector<float>.One);
            var weight1 = Vector.ConditionalSelect(Vector.LessThan(depth1, Vector<float>.Zero), Vector<float>.Zero, Vector<float>.One);
            var weightSum = weight0 + weight1;
            var useFallback = Vector.Equals(weightSum, Vector<float>.Zero);
            weightSum = Vector.ConditionalSelect(useFallback, new Vector<float>(2), weightSum);
            var inverseWeightSum = Vector<float>.One / weightSum;
            weight0 = Vector.ConditionalSelect(useFallback, inverseWeightSum, weight0 * inverseWeightSum);
            weight1 = Vector.ConditionalSelect(useFallback, inverseWeightSum, weight1 * inverseWeightSum);
            Vector3Wide.Scale(offsetA0, weight0, out var a0Contribution);
            Vector3Wide.Scale(offsetA1, weight1, out var a1Contribution);
            Vector3Wide.Add(a0Contribution, a1Contribution, out center);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Prestep(Bodies bodies, ref TwoBodyReferences bodyReferences, int count,
            float dt, float inverseDt, ref BodyInertias inertiaA, ref BodyInertias inertiaB, ref Contact2PrestepData prestep, out Contact2Projection projection)
        {
            //Be careful about the execution order here. It should be aligned with the prestep data layout to ensure prefetching works well.
            projection.InertiaA = inertiaA;
            projection.InertiaB = inertiaB;
            ComputeFrictionCenter(prestep.OffsetA0, prestep.OffsetA1, prestep.PenetrationDepth0, prestep.PenetrationDepth1, out var offsetToManifoldCenterA);
            Vector3Wide.Subtract(offsetToManifoldCenterA, prestep.OffsetB, out var offsetToManifoldCenterB);
            projection.PremultipliedFrictionCoefficient = (1f / 2f) * prestep.FrictionCoefficient;
            projection.Normal = prestep.Normal;
            Helpers.BuildOrthnormalBasis(ref prestep.Normal, out var x, out var z);
            TangentFriction.Prestep(ref x, ref z, ref offsetToManifoldCenterA, ref offsetToManifoldCenterB, ref projection.InertiaA, ref projection.InertiaB, out projection.Tangent);
            PenetrationLimit2.Prestep(ref projection.InertiaA, ref projection.InertiaB, ref prestep, dt, inverseDt, out projection.Penetration);
            //Just assume the lever arms for B are the same. It's a good guess. (The only reason we computed the offset B is because we didn't want to go into world space.)
            Vector3Wide.Distance(prestep.OffsetA0, offsetToManifoldCenterA, out projection.LeverArm0);
            Vector3Wide.Distance(prestep.OffsetA1, offsetToManifoldCenterA, out projection.LeverArm1);
            TwistFriction.Prestep(ref projection.InertiaA, ref projection.InertiaB, ref prestep.Normal, out projection.Twist);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WarmStart(ref BodyVelocities wsvA, ref BodyVelocities wsvB, ref Contact2Projection projection, ref Contact2AccumulatedImpulses accumulatedImpulses)
        {
            Helpers.BuildOrthnormalBasis(ref projection.Normal, out var x, out var z);
            TangentFriction.WarmStart(ref x, ref z, ref projection.Tangent, ref projection.InertiaA, ref projection.InertiaB, ref accumulatedImpulses.Tangent, ref wsvA, ref wsvB);
            PenetrationLimit2.WarmStart(ref projection.Penetration, ref projection.InertiaA, ref projection.InertiaB,
                ref projection.Normal,
                ref accumulatedImpulses.Penetration0,
                ref accumulatedImpulses.Penetration1,
				ref wsvA, ref wsvB);
            TwistFriction.WarmStart(ref projection.Normal, ref projection.InertiaA, ref projection.InertiaB, ref accumulatedImpulses.Twist, ref wsvA, ref wsvB);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Solve(ref BodyVelocities wsvA, ref BodyVelocities wsvB, ref Contact2Projection projection, ref Contact2AccumulatedImpulses accumulatedImpulses)
        {
            Helpers.BuildOrthnormalBasis(ref projection.Normal, out var x, out var z);
            var maximumTangentImpulse = projection.PremultipliedFrictionCoefficient *
                (accumulatedImpulses.Penetration0 + accumulatedImpulses.Penetration1);
            TangentFriction.Solve(ref x, ref z, ref projection.Tangent, ref projection.InertiaA, ref projection.InertiaB, ref maximumTangentImpulse, ref accumulatedImpulses.Tangent, ref wsvA, ref wsvB);
            //Note that we solve the penetration constraints after the friction constraints. 
            //This makes the penetration constraints more authoritative at the cost of the first iteration of the first frame of an impact lacking friction influence.
            //It's a pretty minor effect either way.
            PenetrationLimit2.Solve(ref projection.Penetration, ref projection.InertiaA, ref projection.InertiaB, ref projection.Normal,
                ref accumulatedImpulses.Penetration0,
                ref accumulatedImpulses.Penetration1,
				ref wsvA, ref wsvB);
            var maximumTwistImpulse = projection.PremultipliedFrictionCoefficient * (
                accumulatedImpulses.Penetration0 * projection.LeverArm0 +
                accumulatedImpulses.Penetration1 * projection.LeverArm1);
            TwistFriction.Solve(ref projection.Normal, ref projection.InertiaA, ref projection.InertiaB, ref projection.Twist, ref maximumTwistImpulse, ref accumulatedImpulses.Twist, ref wsvA, ref wsvB);
        }

    }
	
    /// <summary>
    /// Handles the solve iterations of a bunch of 2-contact two body manifold constraints.
    /// </summary>
    public class Contact2TypeProcessor :
        TwoBodyTypeProcessor<Contact2PrestepData, Contact2Projection, Contact2AccumulatedImpulses, Contact2Functions>
    {
        //Matches UpdateConstraintForManifold's manifoldTypeAsConstraintType computation.
        public const int BatchTypeId = 5;
    }


    public struct Contact3 : IConvexTwoBodyContactConstraintDescription<Contact3>
    {
        public ConstraintContactData Contact0;
        public ConstraintContactData Contact1;
        public ConstraintContactData Contact2;
        public Vector3 OffsetB;
        public float FrictionCoefficient;
        public Vector3 Normal;
        public SpringSettings SpringSettings;
        public float MaximumRecoveryVelocity;

        public void ApplyDescription(ref TypeBatch batch, int bundleIndex, int innerIndex)
        {
		    Debug.Assert(batch.TypeId == ConstraintTypeId, "The type batch passed to the description must match the description's expected type.");
            ref var target = ref GetOffsetInstance(ref Buffer<Contact3PrestepData>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            Vector3Wide.WriteFirst(Contact0.OffsetA, ref target.OffsetA0);
            GetFirst(ref target.PenetrationDepth0) = Contact0.PenetrationDepth;
            Vector3Wide.WriteFirst(Contact1.OffsetA, ref target.OffsetA1);
            GetFirst(ref target.PenetrationDepth1) = Contact1.PenetrationDepth;
            Vector3Wide.WriteFirst(Contact2.OffsetA, ref target.OffsetA2);
            GetFirst(ref target.PenetrationDepth2) = Contact2.PenetrationDepth;
			
            Vector3Wide.WriteFirst(OffsetB, ref target.OffsetB);

            GetFirst(ref target.FrictionCoefficient) = FrictionCoefficient;
			
            Vector3Wide.WriteFirst(Normal, ref target.Normal);
			
            SpringSettingsWide.WriteFirst(SpringSettings, ref target.SpringSettings);
            GetFirst(ref target.MaximumRecoveryVelocity) = MaximumRecoveryVelocity;					
        }

        public void BuildDescription(ref TypeBatch batch, int bundleIndex, int innerIndex, out Contact3 description)
        {    
			Debug.Assert(batch.TypeId == ConstraintTypeId, "The type batch passed to the description must match the description's expected type.");
            ref var source = ref GetOffsetInstance(ref Buffer<Contact4PrestepData>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            Vector3Wide.ReadFirst(source.OffsetA0, out description.Contact0.OffsetA);
            description.Contact0.PenetrationDepth = GetFirst(ref source.PenetrationDepth0);
            Vector3Wide.ReadFirst(source.OffsetA1, out description.Contact1.OffsetA);
            description.Contact1.PenetrationDepth = GetFirst(ref source.PenetrationDepth1);
            Vector3Wide.ReadFirst(source.OffsetA2, out description.Contact2.OffsetA);
            description.Contact2.PenetrationDepth = GetFirst(ref source.PenetrationDepth2);
			
            Vector3Wide.ReadFirst(source.OffsetB, out description.OffsetB);

            description.FrictionCoefficient = GetFirst(ref source.FrictionCoefficient);
			
            Vector3Wide.ReadFirst(source.Normal, out description.Normal);
			
            SpringSettingsWide.ReadFirst(source.SpringSettings, out description.SpringSettings);
            description.MaximumRecoveryVelocity = GetFirst(ref source.MaximumRecoveryVelocity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyManifoldWideProperties(ref Vector3 offsetB, ref Vector3 normal, ref PairMaterialProperties material)
        {
            OffsetB = offsetB;
            FrictionCoefficient = material.FrictionCoefficient;
            Normal = normal;
            SpringSettings = material.SpringSettings;
            MaximumRecoveryVelocity = material.MaximumRecoveryVelocity;
        }
		
        public int ConstraintTypeId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Contact3TypeProcessor.BatchTypeId;
        }

        public Type BatchType => typeof(Contact3TypeProcessor);

    }

    public struct Contact3PrestepData
    {
        //NOTE: Prestep data memory layout is relied upon by the constraint description for marginally more efficient setting and getting.
        //If you modify this layout, be sure to update the associated ContactManifold4Constraint.
        //Note that this layout is defined by the execution order in the prestep. The function accesses it sequentially to ensure the prefetcher can do its job.
        public Vector3Wide OffsetA0;
        public Vector3Wide OffsetA1;
        public Vector3Wide OffsetA2;
        public Vector3Wide OffsetB;
        public Vector<float> FrictionCoefficient;
        //In a convex manifold, all contacts share the same normal and tangents.
        public Vector3Wide Normal;
        //All contacts also share the spring settings.
        public SpringSettingsWide SpringSettings;
        public Vector<float> MaximumRecoveryVelocity;
        public Vector<float> PenetrationDepth0;
        public Vector<float> PenetrationDepth1;
        public Vector<float> PenetrationDepth2;
    }

    public struct Contact3AccumulatedImpulses
    {
        public Vector2Wide Tangent;
        public Vector<float> Penetration0;
        public Vector<float> Penetration1;
        public Vector<float> Penetration2;
        public Vector<float> Twist;
    }

    public unsafe struct Contact3Projection
    {
        public BodyInertias InertiaA;
        public BodyInertias InertiaB;
        public Vector<float> PremultipliedFrictionCoefficient;
        public Vector3Wide Normal;
        public TangentFriction.Projection Tangent;
        public PenetrationLimit3.Projection Penetration;
        //Lever arms aren't included in the twist projection because the number of arms required varies independently of the twist projection itself.
        public Vector<float> LeverArm0;
        public Vector<float> LeverArm1;
        public Vector<float> LeverArm2;
        public TwistFrictionProjection Twist;
    }

	public struct Contact3Functions : IConstraintFunctions<Contact3PrestepData, Contact3Projection, Contact3AccumulatedImpulses>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComputeFrictionCenter(
			in Vector3Wide offsetA0, in Vector3Wide offsetA1, in Vector3Wide offsetA2, 
            in Vector<float> depth0,in Vector<float> depth1,in Vector<float> depth2, out Vector3Wide center)
        {
            //This can sometimes cause a weird center of friction. That's a bit strange, but the alternative is often stranger:
            //Without this, if one contact is active and the other is speculative, friction will use the manifold center as halfway between the two points. If something is holding 
            //the inactive contact side up and swinging it around, the existence of speculative contacts would make friction work against the free swinging.
            var weight0 = Vector.ConditionalSelect(Vector.LessThan(depth0, Vector<float>.Zero), Vector<float>.Zero, Vector<float>.One);
            var weight1 = Vector.ConditionalSelect(Vector.LessThan(depth1, Vector<float>.Zero), Vector<float>.Zero, Vector<float>.One);
            var weight2 = Vector.ConditionalSelect(Vector.LessThan(depth2, Vector<float>.Zero), Vector<float>.Zero, Vector<float>.One);
            var weightSum = weight0 + weight1 + weight2;
            var useFallback = Vector.Equals(weightSum, Vector<float>.Zero);
            weightSum = Vector.ConditionalSelect(useFallback, new Vector<float>(3), weightSum);
            var inverseWeightSum = Vector<float>.One / weightSum;
            weight0 = Vector.ConditionalSelect(useFallback, inverseWeightSum, weight0 * inverseWeightSum);
            weight1 = Vector.ConditionalSelect(useFallback, inverseWeightSum, weight1 * inverseWeightSum);
            weight2 = Vector.ConditionalSelect(useFallback, inverseWeightSum, weight2 * inverseWeightSum);
            Vector3Wide.Scale(offsetA0, weight0, out var a0Contribution);
            Vector3Wide.Scale(offsetA1, weight1, out var a1Contribution);
            Vector3Wide.Scale(offsetA2, weight2, out var a2Contribution);
            Vector3Wide.Add(a0Contribution, a1Contribution, out var a0a1);
            Vector3Wide.Add(a0a1, a2Contribution, out center);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Prestep(Bodies bodies, ref TwoBodyReferences bodyReferences, int count,
            float dt, float inverseDt, ref BodyInertias inertiaA, ref BodyInertias inertiaB, ref Contact3PrestepData prestep, out Contact3Projection projection)
        {
            //Be careful about the execution order here. It should be aligned with the prestep data layout to ensure prefetching works well.
            projection.InertiaA = inertiaA;
            projection.InertiaB = inertiaB;
            ComputeFrictionCenter(prestep.OffsetA0, prestep.OffsetA1, prestep.OffsetA2, prestep.PenetrationDepth0, prestep.PenetrationDepth1, prestep.PenetrationDepth2, out var offsetToManifoldCenterA);
            Vector3Wide.Subtract(offsetToManifoldCenterA, prestep.OffsetB, out var offsetToManifoldCenterB);
            projection.PremultipliedFrictionCoefficient = (1f / 3f) * prestep.FrictionCoefficient;
            projection.Normal = prestep.Normal;
            Helpers.BuildOrthnormalBasis(ref prestep.Normal, out var x, out var z);
            TangentFriction.Prestep(ref x, ref z, ref offsetToManifoldCenterA, ref offsetToManifoldCenterB, ref projection.InertiaA, ref projection.InertiaB, out projection.Tangent);
            PenetrationLimit3.Prestep(ref projection.InertiaA, ref projection.InertiaB, ref prestep, dt, inverseDt, out projection.Penetration);
            //Just assume the lever arms for B are the same. It's a good guess. (The only reason we computed the offset B is because we didn't want to go into world space.)
            Vector3Wide.Distance(prestep.OffsetA0, offsetToManifoldCenterA, out projection.LeverArm0);
            Vector3Wide.Distance(prestep.OffsetA1, offsetToManifoldCenterA, out projection.LeverArm1);
            Vector3Wide.Distance(prestep.OffsetA2, offsetToManifoldCenterA, out projection.LeverArm2);
            TwistFriction.Prestep(ref projection.InertiaA, ref projection.InertiaB, ref prestep.Normal, out projection.Twist);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WarmStart(ref BodyVelocities wsvA, ref BodyVelocities wsvB, ref Contact3Projection projection, ref Contact3AccumulatedImpulses accumulatedImpulses)
        {
            Helpers.BuildOrthnormalBasis(ref projection.Normal, out var x, out var z);
            TangentFriction.WarmStart(ref x, ref z, ref projection.Tangent, ref projection.InertiaA, ref projection.InertiaB, ref accumulatedImpulses.Tangent, ref wsvA, ref wsvB);
            PenetrationLimit3.WarmStart(ref projection.Penetration, ref projection.InertiaA, ref projection.InertiaB,
                ref projection.Normal,
                ref accumulatedImpulses.Penetration0,
                ref accumulatedImpulses.Penetration1,
                ref accumulatedImpulses.Penetration2,
				ref wsvA, ref wsvB);
            TwistFriction.WarmStart(ref projection.Normal, ref projection.InertiaA, ref projection.InertiaB, ref accumulatedImpulses.Twist, ref wsvA, ref wsvB);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Solve(ref BodyVelocities wsvA, ref BodyVelocities wsvB, ref Contact3Projection projection, ref Contact3AccumulatedImpulses accumulatedImpulses)
        {
            Helpers.BuildOrthnormalBasis(ref projection.Normal, out var x, out var z);
            var maximumTangentImpulse = projection.PremultipliedFrictionCoefficient *
                (accumulatedImpulses.Penetration0 + accumulatedImpulses.Penetration1 + accumulatedImpulses.Penetration2);
            TangentFriction.Solve(ref x, ref z, ref projection.Tangent, ref projection.InertiaA, ref projection.InertiaB, ref maximumTangentImpulse, ref accumulatedImpulses.Tangent, ref wsvA, ref wsvB);
            //Note that we solve the penetration constraints after the friction constraints. 
            //This makes the penetration constraints more authoritative at the cost of the first iteration of the first frame of an impact lacking friction influence.
            //It's a pretty minor effect either way.
            PenetrationLimit3.Solve(ref projection.Penetration, ref projection.InertiaA, ref projection.InertiaB, ref projection.Normal,
                ref accumulatedImpulses.Penetration0,
                ref accumulatedImpulses.Penetration1,
                ref accumulatedImpulses.Penetration2,
				ref wsvA, ref wsvB);
            var maximumTwistImpulse = projection.PremultipliedFrictionCoefficient * (
                accumulatedImpulses.Penetration0 * projection.LeverArm0 +
                accumulatedImpulses.Penetration1 * projection.LeverArm1 +
                accumulatedImpulses.Penetration2 * projection.LeverArm2);
            TwistFriction.Solve(ref projection.Normal, ref projection.InertiaA, ref projection.InertiaB, ref projection.Twist, ref maximumTwistImpulse, ref accumulatedImpulses.Twist, ref wsvA, ref wsvB);
        }

    }
	
    /// <summary>
    /// Handles the solve iterations of a bunch of 3-contact two body manifold constraints.
    /// </summary>
    public class Contact3TypeProcessor :
        TwoBodyTypeProcessor<Contact3PrestepData, Contact3Projection, Contact3AccumulatedImpulses, Contact3Functions>
    {
        //Matches UpdateConstraintForManifold's manifoldTypeAsConstraintType computation.
        public const int BatchTypeId = 6;
    }


    public struct Contact4 : IConvexTwoBodyContactConstraintDescription<Contact4>
    {
        public ConstraintContactData Contact0;
        public ConstraintContactData Contact1;
        public ConstraintContactData Contact2;
        public ConstraintContactData Contact3;
        public Vector3 OffsetB;
        public float FrictionCoefficient;
        public Vector3 Normal;
        public SpringSettings SpringSettings;
        public float MaximumRecoveryVelocity;

        public void ApplyDescription(ref TypeBatch batch, int bundleIndex, int innerIndex)
        {
		    Debug.Assert(batch.TypeId == ConstraintTypeId, "The type batch passed to the description must match the description's expected type.");
            ref var target = ref GetOffsetInstance(ref Buffer<Contact4PrestepData>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            Vector3Wide.WriteFirst(Contact0.OffsetA, ref target.OffsetA0);
            GetFirst(ref target.PenetrationDepth0) = Contact0.PenetrationDepth;
            Vector3Wide.WriteFirst(Contact1.OffsetA, ref target.OffsetA1);
            GetFirst(ref target.PenetrationDepth1) = Contact1.PenetrationDepth;
            Vector3Wide.WriteFirst(Contact2.OffsetA, ref target.OffsetA2);
            GetFirst(ref target.PenetrationDepth2) = Contact2.PenetrationDepth;
            Vector3Wide.WriteFirst(Contact3.OffsetA, ref target.OffsetA3);
            GetFirst(ref target.PenetrationDepth3) = Contact3.PenetrationDepth;
			
            Vector3Wide.WriteFirst(OffsetB, ref target.OffsetB);

            GetFirst(ref target.FrictionCoefficient) = FrictionCoefficient;
			
            Vector3Wide.WriteFirst(Normal, ref target.Normal);
			
            SpringSettingsWide.WriteFirst(SpringSettings, ref target.SpringSettings);
            GetFirst(ref target.MaximumRecoveryVelocity) = MaximumRecoveryVelocity;					
        }

        public void BuildDescription(ref TypeBatch batch, int bundleIndex, int innerIndex, out Contact4 description)
        {    
			Debug.Assert(batch.TypeId == ConstraintTypeId, "The type batch passed to the description must match the description's expected type.");
            ref var source = ref GetOffsetInstance(ref Buffer<Contact4PrestepData>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            Vector3Wide.ReadFirst(source.OffsetA0, out description.Contact0.OffsetA);
            description.Contact0.PenetrationDepth = GetFirst(ref source.PenetrationDepth0);
            Vector3Wide.ReadFirst(source.OffsetA1, out description.Contact1.OffsetA);
            description.Contact1.PenetrationDepth = GetFirst(ref source.PenetrationDepth1);
            Vector3Wide.ReadFirst(source.OffsetA2, out description.Contact2.OffsetA);
            description.Contact2.PenetrationDepth = GetFirst(ref source.PenetrationDepth2);
            Vector3Wide.ReadFirst(source.OffsetA3, out description.Contact3.OffsetA);
            description.Contact3.PenetrationDepth = GetFirst(ref source.PenetrationDepth3);
			
            Vector3Wide.ReadFirst(source.OffsetB, out description.OffsetB);

            description.FrictionCoefficient = GetFirst(ref source.FrictionCoefficient);
			
            Vector3Wide.ReadFirst(source.Normal, out description.Normal);
			
            SpringSettingsWide.ReadFirst(source.SpringSettings, out description.SpringSettings);
            description.MaximumRecoveryVelocity = GetFirst(ref source.MaximumRecoveryVelocity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyManifoldWideProperties(ref Vector3 offsetB, ref Vector3 normal, ref PairMaterialProperties material)
        {
            OffsetB = offsetB;
            FrictionCoefficient = material.FrictionCoefficient;
            Normal = normal;
            SpringSettings = material.SpringSettings;
            MaximumRecoveryVelocity = material.MaximumRecoveryVelocity;
        }
		
        public int ConstraintTypeId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Contact4TypeProcessor.BatchTypeId;
        }

        public Type BatchType => typeof(Contact4TypeProcessor);

    }

    public struct Contact4PrestepData
    {
        //NOTE: Prestep data memory layout is relied upon by the constraint description for marginally more efficient setting and getting.
        //If you modify this layout, be sure to update the associated ContactManifold4Constraint.
        //Note that this layout is defined by the execution order in the prestep. The function accesses it sequentially to ensure the prefetcher can do its job.
        public Vector3Wide OffsetA0;
        public Vector3Wide OffsetA1;
        public Vector3Wide OffsetA2;
        public Vector3Wide OffsetA3;
        public Vector3Wide OffsetB;
        public Vector<float> FrictionCoefficient;
        //In a convex manifold, all contacts share the same normal and tangents.
        public Vector3Wide Normal;
        //All contacts also share the spring settings.
        public SpringSettingsWide SpringSettings;
        public Vector<float> MaximumRecoveryVelocity;
        public Vector<float> PenetrationDepth0;
        public Vector<float> PenetrationDepth1;
        public Vector<float> PenetrationDepth2;
        public Vector<float> PenetrationDepth3;
    }

    public struct Contact4AccumulatedImpulses
    {
        public Vector2Wide Tangent;
        public Vector<float> Penetration0;
        public Vector<float> Penetration1;
        public Vector<float> Penetration2;
        public Vector<float> Penetration3;
        public Vector<float> Twist;
    }

    public unsafe struct Contact4Projection
    {
        public BodyInertias InertiaA;
        public BodyInertias InertiaB;
        public Vector<float> PremultipliedFrictionCoefficient;
        public Vector3Wide Normal;
        public TangentFriction.Projection Tangent;
        public PenetrationLimit4.Projection Penetration;
        //Lever arms aren't included in the twist projection because the number of arms required varies independently of the twist projection itself.
        public Vector<float> LeverArm0;
        public Vector<float> LeverArm1;
        public Vector<float> LeverArm2;
        public Vector<float> LeverArm3;
        public TwistFrictionProjection Twist;
    }

	public struct Contact4Functions : IConstraintFunctions<Contact4PrestepData, Contact4Projection, Contact4AccumulatedImpulses>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComputeFrictionCenter(
			in Vector3Wide offsetA0, in Vector3Wide offsetA1, in Vector3Wide offsetA2, in Vector3Wide offsetA3, 
            in Vector<float> depth0,in Vector<float> depth1,in Vector<float> depth2,in Vector<float> depth3, out Vector3Wide center)
        {
            //This can sometimes cause a weird center of friction. That's a bit strange, but the alternative is often stranger:
            //Without this, if one contact is active and the other is speculative, friction will use the manifold center as halfway between the two points. If something is holding 
            //the inactive contact side up and swinging it around, the existence of speculative contacts would make friction work against the free swinging.
            var weight0 = Vector.ConditionalSelect(Vector.LessThan(depth0, Vector<float>.Zero), Vector<float>.Zero, Vector<float>.One);
            var weight1 = Vector.ConditionalSelect(Vector.LessThan(depth1, Vector<float>.Zero), Vector<float>.Zero, Vector<float>.One);
            var weight2 = Vector.ConditionalSelect(Vector.LessThan(depth2, Vector<float>.Zero), Vector<float>.Zero, Vector<float>.One);
            var weight3 = Vector.ConditionalSelect(Vector.LessThan(depth3, Vector<float>.Zero), Vector<float>.Zero, Vector<float>.One);
            var weightSum = weight0 + weight1 + weight2 + weight3;
            var useFallback = Vector.Equals(weightSum, Vector<float>.Zero);
            weightSum = Vector.ConditionalSelect(useFallback, new Vector<float>(4), weightSum);
            var inverseWeightSum = Vector<float>.One / weightSum;
            weight0 = Vector.ConditionalSelect(useFallback, inverseWeightSum, weight0 * inverseWeightSum);
            weight1 = Vector.ConditionalSelect(useFallback, inverseWeightSum, weight1 * inverseWeightSum);
            weight2 = Vector.ConditionalSelect(useFallback, inverseWeightSum, weight2 * inverseWeightSum);
            weight3 = Vector.ConditionalSelect(useFallback, inverseWeightSum, weight3 * inverseWeightSum);
            Vector3Wide.Scale(offsetA0, weight0, out var a0Contribution);
            Vector3Wide.Scale(offsetA1, weight1, out var a1Contribution);
            Vector3Wide.Scale(offsetA2, weight2, out var a2Contribution);
            Vector3Wide.Scale(offsetA3, weight3, out var a3Contribution);
            Vector3Wide.Add(a0Contribution, a1Contribution, out var a0a1);
            Vector3Wide.Add(a2Contribution, a3Contribution, out var a2a3);
            Vector3Wide.Add(a0a1, a2a3, out center);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Prestep(Bodies bodies, ref TwoBodyReferences bodyReferences, int count,
            float dt, float inverseDt, ref BodyInertias inertiaA, ref BodyInertias inertiaB, ref Contact4PrestepData prestep, out Contact4Projection projection)
        {
            //Be careful about the execution order here. It should be aligned with the prestep data layout to ensure prefetching works well.
            projection.InertiaA = inertiaA;
            projection.InertiaB = inertiaB;
            ComputeFrictionCenter(prestep.OffsetA0, prestep.OffsetA1, prestep.OffsetA2, prestep.OffsetA3, prestep.PenetrationDepth0, prestep.PenetrationDepth1, prestep.PenetrationDepth2, prestep.PenetrationDepth3, out var offsetToManifoldCenterA);
            Vector3Wide.Subtract(offsetToManifoldCenterA, prestep.OffsetB, out var offsetToManifoldCenterB);
            projection.PremultipliedFrictionCoefficient = (1f / 4f) * prestep.FrictionCoefficient;
            projection.Normal = prestep.Normal;
            Helpers.BuildOrthnormalBasis(ref prestep.Normal, out var x, out var z);
            TangentFriction.Prestep(ref x, ref z, ref offsetToManifoldCenterA, ref offsetToManifoldCenterB, ref projection.InertiaA, ref projection.InertiaB, out projection.Tangent);
            PenetrationLimit4.Prestep(ref projection.InertiaA, ref projection.InertiaB, ref prestep, dt, inverseDt, out projection.Penetration);
            //Just assume the lever arms for B are the same. It's a good guess. (The only reason we computed the offset B is because we didn't want to go into world space.)
            Vector3Wide.Distance(prestep.OffsetA0, offsetToManifoldCenterA, out projection.LeverArm0);
            Vector3Wide.Distance(prestep.OffsetA1, offsetToManifoldCenterA, out projection.LeverArm1);
            Vector3Wide.Distance(prestep.OffsetA2, offsetToManifoldCenterA, out projection.LeverArm2);
            Vector3Wide.Distance(prestep.OffsetA3, offsetToManifoldCenterA, out projection.LeverArm3);
            TwistFriction.Prestep(ref projection.InertiaA, ref projection.InertiaB, ref prestep.Normal, out projection.Twist);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WarmStart(ref BodyVelocities wsvA, ref BodyVelocities wsvB, ref Contact4Projection projection, ref Contact4AccumulatedImpulses accumulatedImpulses)
        {
            Helpers.BuildOrthnormalBasis(ref projection.Normal, out var x, out var z);
            TangentFriction.WarmStart(ref x, ref z, ref projection.Tangent, ref projection.InertiaA, ref projection.InertiaB, ref accumulatedImpulses.Tangent, ref wsvA, ref wsvB);
            PenetrationLimit4.WarmStart(ref projection.Penetration, ref projection.InertiaA, ref projection.InertiaB,
                ref projection.Normal,
                ref accumulatedImpulses.Penetration0,
                ref accumulatedImpulses.Penetration1,
                ref accumulatedImpulses.Penetration2,
                ref accumulatedImpulses.Penetration3,
				ref wsvA, ref wsvB);
            TwistFriction.WarmStart(ref projection.Normal, ref projection.InertiaA, ref projection.InertiaB, ref accumulatedImpulses.Twist, ref wsvA, ref wsvB);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Solve(ref BodyVelocities wsvA, ref BodyVelocities wsvB, ref Contact4Projection projection, ref Contact4AccumulatedImpulses accumulatedImpulses)
        {
            Helpers.BuildOrthnormalBasis(ref projection.Normal, out var x, out var z);
            var maximumTangentImpulse = projection.PremultipliedFrictionCoefficient *
                (accumulatedImpulses.Penetration0 + accumulatedImpulses.Penetration1 + accumulatedImpulses.Penetration2 + accumulatedImpulses.Penetration3);
            TangentFriction.Solve(ref x, ref z, ref projection.Tangent, ref projection.InertiaA, ref projection.InertiaB, ref maximumTangentImpulse, ref accumulatedImpulses.Tangent, ref wsvA, ref wsvB);
            //Note that we solve the penetration constraints after the friction constraints. 
            //This makes the penetration constraints more authoritative at the cost of the first iteration of the first frame of an impact lacking friction influence.
            //It's a pretty minor effect either way.
            PenetrationLimit4.Solve(ref projection.Penetration, ref projection.InertiaA, ref projection.InertiaB, ref projection.Normal,
                ref accumulatedImpulses.Penetration0,
                ref accumulatedImpulses.Penetration1,
                ref accumulatedImpulses.Penetration2,
                ref accumulatedImpulses.Penetration3,
				ref wsvA, ref wsvB);
            var maximumTwistImpulse = projection.PremultipliedFrictionCoefficient * (
                accumulatedImpulses.Penetration0 * projection.LeverArm0 +
                accumulatedImpulses.Penetration1 * projection.LeverArm1 +
                accumulatedImpulses.Penetration2 * projection.LeverArm2 +
                accumulatedImpulses.Penetration3 * projection.LeverArm3);
            TwistFriction.Solve(ref projection.Normal, ref projection.InertiaA, ref projection.InertiaB, ref projection.Twist, ref maximumTwistImpulse, ref accumulatedImpulses.Twist, ref wsvA, ref wsvB);
        }

    }
	
    /// <summary>
    /// Handles the solve iterations of a bunch of 4-contact two body manifold constraints.
    /// </summary>
    public class Contact4TypeProcessor :
        TwoBodyTypeProcessor<Contact4PrestepData, Contact4Projection, Contact4AccumulatedImpulses, Contact4Functions>
    {
        //Matches UpdateConstraintForManifold's manifoldTypeAsConstraintType computation.
        public const int BatchTypeId = 7;
    }


}