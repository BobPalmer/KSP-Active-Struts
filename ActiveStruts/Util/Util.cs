﻿using System;
using System.Collections.Generic;
using System.Linq;
using ActiveStruts.Modules;
using UnityEngine;

namespace ActiveStruts.Util
{
    public class RaycastResult
    {
        public float DistanceFromOrigin { get; set; }
        public RaycastHit Hit { get; set; }
        public bool HitResult { get; set; }
        public Part HittedPart { get; set; }
        public Ray Ray { get; set; }
        public float RayAngle { get; set; }
    }

    public class FreeAttachTargetCheck
    {
        public bool HitResult { get; set; }
        public Part TargetPart { get; set; }
    }

    public static class Util
    {
        public static bool AnyTargetersConnected(this ModuleActiveStrut target)
        {
            return GetAllActiveStruts().Any(m => !m.IsTargetOnly && m.Mode == Mode.Linked && m.Target != null && m.Target == target);
        }

        public static FreeAttachTargetCheck CheckFreeAttachPoint(this ModuleActiveStrut origin)
        {
            var raycast = PerformRaycast(origin.Origin.position, origin.FreeAttachTarget.PartOrigin.position, origin.Origin.right*-1);
            if (raycast.HitResult)
            {
                var distOk = raycast.DistanceFromOrigin <= Config.Instance.MaxDistance;
                return new FreeAttachTargetCheck
                       {
                           TargetPart = raycast.HittedPart,
                           HitResult = distOk
                       };
            }
            return new FreeAttachTargetCheck
                   {
                       TargetPart = null,
                       HitResult = false
                   };
        }

        public static bool DistanceInToleranceRange(float savedDistance, float currentDistance)
        {
            return currentDistance >= savedDistance - Config.Instance.FreeAttachDistanceTolerance && currentDistance <= savedDistance + Config.Instance.FreeAttachDistanceTolerance &&
                   currentDistance <= Config.Instance.MaxDistance;
        }

        public static bool EditorAboutToAttach(bool moveToo = false)
        {
            return HighLogic.LoadedSceneIsEditor &&
                   EditorLogic.SelectedPart != null &&
                   (EditorLogic.SelectedPart.potentialParent != null ||
                    (moveToo && EditorLogic.SelectedPart == EditorLogic.startPod));
        }

        public static ModuleActiveStrutFreeAttachTarget FindFreeAttachTarget(Guid guid)
        {
            return GetAllFreeAttachTargets().Find(m => m.ID == guid);
        }

        public static List<ModuleActiveStrut> GetAllActiveStruts()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                var allParts = FlightGlobals.Vessels.SelectMany(v => v.parts).ToList();
                return allParts.Where(p => p.Modules.Contains(Config.Instance.ModuleName)).Select(p => p.Modules[Config.Instance.ModuleName] as ModuleActiveStrut).ToList();
            }
            if (!HighLogic.LoadedSceneIsEditor)
            {
                return new List<ModuleActiveStrut>();
            }
            var partList = ListEditorParts(true);
            return partList.Where(p => p.Modules.Contains(Config.Instance.ModuleName)).Select(p => p.Modules[Config.Instance.ModuleName] as ModuleActiveStrut).ToList();
        }

        public static List<ModuleActiveStrut> GetAllConnectedTargeters(this ModuleActiveStrut target)
        {
            return GetAllActiveStruts().Where(m => !m.IsTargetOnly && m.Mode == Mode.Linked && m.Target != null && m.Target == target).ToList();
        }

        public static List<ModuleActiveStrutFreeAttachTarget> GetAllFreeAttachTargets()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                var allParts = FlightGlobals.Vessels.SelectMany(v => v.parts).ToList();
                return
                    allParts.Where(p => p.Modules.Contains(Config.Instance.ModuleActiveStrutFreeAttachTarget))
                            .Select(p => p.Modules[Config.Instance.ModuleActiveStrutFreeAttachTarget] as ModuleActiveStrutFreeAttachTarget)
                            .ToList();
            }
            if (!HighLogic.LoadedSceneIsEditor)
            {
                return new List<ModuleActiveStrutFreeAttachTarget>();
            }
            var partList = ListEditorParts(true);
            return partList.Where(p => p.Modules.Contains(Config.Instance.ModuleActiveStrutFreeAttachTarget)).Select(p => p.Modules[Config.Instance.ModuleActiveStrutFreeAttachTarget] as ModuleActiveStrutFreeAttachTarget).ToList();
        }

        public static List<ModuleActiveStrut> GetAllPossibleTargets(this ModuleActiveStrut origin)
        {
            return GetAllActiveStruts().Where(m => m.ID != origin.ID && origin.IsPossibleTarget(m)).Select(m => m).ToList();
        }

        public static float GetJointStrength(this LinkType type)
        {
            switch (type)
            {
                case LinkType.None:
                {
                    return 0;
                }
                case LinkType.Normal:
                {
                    return Config.Instance.NormalJointStrength;
                }
                case LinkType.Maximum:
                {
                    return Config.Instance.MaximalJointStrength;
                }
                case LinkType.Weak:
                {
                    return Config.Instance.WeakJointStrength;
                }
            }
            return 0;
        }

        public static Vector3 GetMouseWorldPosition()
        {
            var ray = HighLogic.LoadedSceneIsFlight ? FlightCamera.fetch.mainCamera.ScreenPointToRay(Input.mousePosition) : Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            return Physics.Raycast(ray, out hit, Config.Instance.MaxDistance) ? hit.point : Vector3.zero;
        }

        public static Vector3 GetNewWorldPosForFreeAttachTarget(Part freeAttachPart, Vector3 freeAttachTargetLocalVector)
        {
            if (freeAttachPart == null)
            {
                return Vector3.zero;
            }
            var newPoint = freeAttachPart.transform.position + freeAttachTargetLocalVector;
            return newPoint;
        }

        public static float[] GetRgbaFromColor(Color color)
        {
            var ret = new float[4];
            ret[0] = color.r;
            ret[1] = color.g;
            ret[2] = color.b;
            ret[3] = color.a;
            return ret;
        }

        public static ModuleActiveStrut GetStrutById(Guid id)
        {
            return GetAllActiveStruts().Find(m => m.ID == id);
        }

        public static bool IsPossibleFreeAttachTarget(this ModuleActiveStrut origin, Vector3 mousePosition)
        {
            var raycast = PerformRaycast(origin.Origin.position, mousePosition, origin.Origin.right*-1);
            return raycast.HitResult && raycast.DistanceFromOrigin <= Config.Instance.MaxDistance && raycast.RayAngle <= Config.Instance.MaxAngle;
        }

        public static bool IsPossibleTarget(this ModuleActiveStrut origin, ModuleActiveStrut possibleTarget)
        {
            if (possibleTarget.IsConnectionFree || (possibleTarget.Targeter != null && possibleTarget.Targeter.ID == origin.ID))
            {
                var raycast = PerformRaycast(origin.Origin.position, possibleTarget.Origin.position, origin.Origin.right*-1);
                return raycast.HitResult && raycast.HittedPart == possibleTarget.part && raycast.DistanceFromOrigin <= Config.Instance.MaxDistance && raycast.RayAngle <= Config.Instance.MaxAngle;
            }
            return false;
        }

        public static List<Part> ListEditorParts(bool includeSelected)
        {
            var list = new List<Part>();
            if (EditorLogic.startPod)
            {
                RecursePartList(list, EditorLogic.startPod);
            }
            if (!includeSelected || !EditorAboutToAttach())
            {
                return list;
            }
            RecursePartList(list, EditorLogic.SelectedPart);
            foreach (var sym in EditorLogic.SelectedPart.symmetryCounterparts)
            {
                RecursePartList(list, sym);
            }
            return list;
        }

        public static Color MakeColorTransparent(Color color)
        {
            var rgba = GetRgbaFromColor(color);
            return new Color(rgba[0], rgba[1], rgba[2], Config.Instance.ColorTransparency);
        }

        public static Part PartFromHit(this RaycastHit hit)
        {
            if (hit.collider == null || hit.collider.gameObject == null)
            {
                return null;
            }
            var go = hit.collider.gameObject;
            var p = Part.FromGO(go);
            while (p == null)
            {
                if (go.transform != null && go.transform.parent != null && go.transform.parent.gameObject != null)
                {
                    go = go.transform.parent.gameObject;
                }
                else
                {
                    break;
                }
                p = Part.FromGO(go);
            }
            return p;
        }

        public static RaycastResult PerformRaycast(Vector3 origin, Vector3 target, Vector3 originUp)
        {
            RaycastHit info;
            var dir = (target - origin).normalized;
            var ray = new Ray(origin, dir);
            var hit = Physics.Raycast(ray, out info, Config.Instance.MaxDistance + 1);
            var hittedPart = hit ? PartFromHit(info) : null;
            hit = hit && hittedPart != null;
            var angle = Vector3.Angle(dir, originUp);
            return new RaycastResult
                   {
                       DistanceFromOrigin = info.distance,
                       Hit = info,
                       HittedPart = hittedPart,
                       HitResult = hit,
                       Ray = ray,
                       RayAngle = angle
                   };
        }

        public static void RecursePartList(ICollection<Part> list, Part part)
        {
            list.Add(part);
            foreach (var p in part.children)
            {
                RecursePartList(list, p);
            }
        }

        public static void ResetAllFromTargeting()
        {
            foreach (var moduleActiveStrut in GetAllActiveStruts().Where(m => m.Mode == Mode.Target))
            {
                moduleActiveStrut.Mode = Mode.Unlinked;
                moduleActiveStrut.part.SetHighlightDefault();
                moduleActiveStrut.UpdateGui();
                moduleActiveStrut.Targeter = moduleActiveStrut.OldTargeter;
            }
        }

        public static void UnlinkAllConnectedTargeters(this ModuleActiveStrut target)
        {
            var allTargeters = target.GetAllConnectedTargeters();
            foreach (var moduleActiveStrut in allTargeters)
            {
                moduleActiveStrut.Unlink();
            }
        }
    }
}