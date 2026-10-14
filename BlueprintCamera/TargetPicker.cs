using System;
using UnityEngine;

namespace BlueprintCamera;

internal static class TargetPicker
{
    public static Vector3 Pick(PlanetData planet, Ray ray)
    {
        float distance = 10000;
        bool found = Phys.RayCastSphere(ray.origin, ray.direction, distance, Vector3.zero, planet.realRadius, out var ground);
        if (found) distance = ground.dist;
        // DSP only instantiates nearby Unity colliders. Use its planet-local collider data
        // for distant factories too. This full scan runs only on an explicit target pick.
        foreach (var chunk in planet.physics.colChunks)
        {
            for (int i = 1; i < chunk.cursor; i++)
            {
                ref ColliderData collider = ref chunk.colliderPool[i];
                if (collider.idType == 0 || !collider.isForBuild || (collider.objType != EObjectType.None && collider.objType != EObjectType.Prebuild)) continue;
                bool hit;
                RCHCPU result;
                switch (collider.shape)
                {
                    case EColliderShape.Box:
                        hit = Phys.RayCastOBB(ray.origin, ray.direction, distance, collider.ext, collider.pos, collider.q, out result);
                        break;
                    case EColliderShape.Sphere:
                        hit = Phys.RayCastSphere(ray.origin, ray.direction, distance, collider.pos, collider.radius, out result);
                        break;
                    case EColliderShape.Capsule:
                        hit = Phys.RayCastCapsule(ray.origin, ray.direction, distance, collider.pos - collider.ext, collider.pos + collider.ext, collider.radius, out result);
                        break;
                    default: continue;
                }
                if (hit && result.dist >= 0 && result.dist < distance) { distance = result.dist; found = true; }
            }
        }
        if (!found) throw new ArgumentException("画面中心未指向建筑或球面。");
        return ray.GetPoint(distance);
    }
}
