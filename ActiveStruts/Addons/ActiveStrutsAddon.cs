﻿using System;
using System.Collections.Generic;
using System.Linq;
using ActiveStruts.Modules;
using ActiveStruts.Util;
using UnityEngine;

namespace ActiveStruts.Addons
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class ActiveStrutsAddon : MonoBehaviour
    {
        private static GameObject _connector;
        private static object _idResetQueueLock;
        private static int _idResetCounter;
        private static bool _idResetTrimFlag;
        private bool _resetAllHighlighting;
        private List<StraightOutHintActivePart> _straightOutHintActiveParts;
        private List<HighlightedPart> _targetHighlightedParts;
        public static ModuleActiveStrut CurrentTargeter { get; set; }
        public static AddonMode Mode { get; set; }
        public static Vector3 Origin { get; set; }
        private static Queue<IDResetable> _idResetQueue { get; set; }

        //must not be static
        private void ActionMenuClosed(Part data)
        {
            if (!_checkForModule(data))
            {
                return;
            }
            if (Mode != AddonMode.None)
            {
                try
                {
                    CurrentTargeter.AbortLink();
                    CurrentTargeter.UpdateGui();
                    Input.ResetInputAxes();
                    InputLockManager.RemoveControlLock(Config.Instance.EditorInputLockId);
                }
                catch (Exception)
                {
                    //sanity reason
                }
            }
            var module = data.Modules[Config.Instance.ModuleName] as ModuleActiveStrut;
            if (module == null)
            {
                return;
            }
            if (module.IsConnectionOrigin && module.Target != null)
            {
                module.Target.part.SetHighlightDefault();
                var part = this._targetHighlightedParts.Where(p => p.ModuleID == module.ID).Select(p => p).FirstOrDefault();
                if (part != null)
                {
                    try
                    {
                        this._targetHighlightedParts.Remove(part);
                    }
                    catch (NullReferenceException)
                    {
                        //multithreading issue orccured here, not sure if fixed
                    }
                }
            }
            else if (module.Target != null && (!module.IsConnectionOrigin && module.Targeter != null))
            {
                module.Targeter.part.SetHighlightDefault();
            }
            if (!Config.Instance.ShowStraightOutHint || module.IsTargetOnly || module.IsLinked)
            {
                return;
            }
            var hintObj = this._straightOutHintActiveParts.Where(sohap => sohap.ModuleID == module.ID).Select(sohap => sohap).FirstOrDefault();
            if (hintObj == null)
            {
                return;
            }
            this._straightOutHintActiveParts.Remove(hintObj);
            Destroy(hintObj.HintObject);
        }

        //must not be static
        private void ActionMenuCreated(Part data)
        {
            if (!_checkForModule(data))
            {
                return;
            }
            var module = data.Modules[Config.Instance.ModuleName] as ModuleActiveStrut;
            if (module == null)
            {
                return;
            }
            if (module.IsConnectionOrigin && module.Target != null)
            {
                module.Target.part.SetHighlightColor(Color.cyan);
                module.Target.part.SetHighlight(true);
                this._targetHighlightedParts.Add(new HighlightedPart(module.Target.part, module.ID));
            }
            else if (module.Targeter != null && !module.IsConnectionOrigin)
            {
                module.Targeter.part.SetHighlightColor(Color.cyan);
                module.Targeter.part.SetHighlight(true);
                this._targetHighlightedParts.Add(new HighlightedPart(module.Targeter.part, module.ID));
            }
            if (Config.Instance.ShowStraightOutHint)
            {
                this._straightOutHintActiveParts.Add(new StraightOutHintActivePart(data, module.ID, CreateStraightOutHintForPart(module), module));
            }
        }

        public void Awake()
        {
            if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight))
            {
                return;
            }
            this._targetHighlightedParts = new List<HighlightedPart>();
            this._straightOutHintActiveParts = new List<StraightOutHintActivePart>();
            _connector = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _connector.name = "ASConn";
            DestroyImmediate(_connector.collider);
            var connDim = Config.Instance.ConnectorDimension;
            _connector.transform.localScale = new Vector3(connDim, connDim, connDim);
            var mr = _connector.GetComponent<MeshRenderer>();
            mr.name = "ASConn";
            mr.material = new Material(Shader.Find("Transparent/Diffuse")) {color = Util.Util.MakeColorTransparent(Color.green)};
            _connector.SetActive(false);
            GameEvents.onPartActionUICreate.Add(this.ActionMenuCreated);
            GameEvents.onPartActionUIDismiss.Add(this.ActionMenuClosed);
            Mode = AddonMode.None;
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onPartRemove.Add(this.HandleEditorPartDetach);
            }
            else if (HighLogic.LoadedSceneIsFlight)
            {
                GameEvents.onPartAttach.Add(this.HandleFlightPartAttach);
                GameEvents.onPartRemove.Add(this.HandleFlightPartAttach);
                _idResetQueueLock = new object();
                _idResetQueue = new Queue<IDResetable>(200);
                _idResetCounter = Config.IdResetCheckInterval;
                _idResetTrimFlag = false;
            }
        }

        private static GameObject CreateStraightOutHintForPart(ModuleActiveStrut module)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.SetActive(false);
            go.name = Guid.NewGuid().ToString();
            DestroyImmediate(go.collider);
            var connDim = Config.Instance.ConnectorDimension;
            go.transform.localScale = new Vector3(connDim, connDim, connDim);
            var mr = go.GetComponent<MeshRenderer>();
            mr.name = go.name;
            mr.material = new Material(Shader.Find("Transparent/Diffuse")) {color = Util.Util.MakeColorTransparent(Color.blue)};
            UpdateStraightOutHint(module, go);
            return go;
        }

        public static IDResetable Dequeue()
        {
            lock (_idResetQueueLock)
            {
                return _idResetQueue.Dequeue();
            }
        }

        public static void Enqueue(IDResetable module)
        {
            lock (_idResetQueueLock)
            {
                _idResetQueue.Enqueue(module);
            }
        }

        private void HandleEditorPartDetach(GameEvents.HostTargetAction<Part, Part> hostTargetAction)
        {
            var partList = new List<Part> {hostTargetAction.target};
            foreach (var child in hostTargetAction.target.children)
            {
                Util.Util.RecursePartList(partList, child);
            }
            var movedModules = (from p in partList
                                where p.Modules.Contains(Config.Instance.ModuleName)
                                select p.Modules[Config.Instance.ModuleName] as ModuleActiveStrut).ToList();
            var movedTargets = (from p in partList
                                where p.Modules.Contains(Config.Instance.ModuleActiveStrutFreeAttachTarget)
                                select p.Modules[Config.Instance.ModuleActiveStrutFreeAttachTarget] as ModuleActiveStrutFreeAttachTarget).ToList();
            var vesselModules = (from p in Util.Util.ListEditorParts(false)
                                 where p.Modules.Contains(Config.Instance.ModuleName)
                                 select p.Modules[Config.Instance.ModuleName] as ModuleActiveStrut).ToList();
            foreach (var module in movedModules)
            {
                module.Unlink();
            }
            foreach (var module in vesselModules.Where(module =>
                                                       (module.Target != null && movedModules.Any(m => m.ID == module.Target.ID) ||
                                                        (module.Targeter != null && movedModules.Any(m => m.ID == module.Targeter.ID))) ||
                                                       (module.IsFreeAttached && movedTargets.Any(t => t.ID == module.FreeAttachTarget.ID))))
            {
                module.Unlink();
            }
        }

        public void HandleFlightPartAttach(GameEvents.HostTargetAction<Part, Part> hostTargetAction)
        {
            try
            {
                if (!FlightGlobals.ActiveVessel.isEVA)
                {
                    return;
                }
                foreach (var module in hostTargetAction.target.GetComponentsInChildren<ModuleActiveStrut>())
                {
                    if (module.IsTargetOnly)
                    {
                        module.UnlinkAllConnectedTargeters();
                    }
                    else
                    {
                        module.Unlink();
                    }
                }
            }
            catch (NullReferenceException)
            {
                //thrown on launch, don't know why
            }
        }

        public void HandleFlightPartUndock(Part data)
        {
            Debug.Log("[AS] part undocked");
        }

        private static bool IsQueueEmpty()
        {
            lock (_idResetQueueLock)
            {
                return _idResetQueue.Count == 0;
            }
        }

        private static bool IsValidPosition(RaycastResult raycast)
        {
            var valid = raycast.HitResult && raycast.HittedPart != null && raycast.DistanceFromOrigin <= Config.Instance.MaxDistance && raycast.RayAngle <= Config.Instance.MaxAngle;
            switch (Mode)
            {
                case AddonMode.Link:
                {
                    if (raycast.HittedPart != null && raycast.HittedPart.Modules.Contains(Config.Instance.ModuleName))
                    {
                        var moduleActiveStrut = raycast.HittedPart.Modules[Config.Instance.ModuleName] as ModuleActiveStrut;
                        if (moduleActiveStrut != null)
                        {
                            valid = valid && raycast.HittedPart != null && raycast.HittedPart.Modules.Contains(Config.Instance.ModuleName) && moduleActiveStrut.IsConnectionFree;
                        }
                    }
                }
                    break;
            }
            return valid;
        }

        public void OnDestroy()
        {
            GameEvents.onPartActionUICreate.Remove(this.ActionMenuCreated);
            GameEvents.onPartActionUIDismiss.Remove(this.ActionMenuClosed);
            GameEvents.onPartRemove.Remove(this.HandleEditorPartDetach);
            GameEvents.onPartUndock.Remove(this.HandleFlightPartUndock);
            GameEvents.onPartAttach.Remove(this.HandleFlightPartAttach);
        }

        private static void TrimQueue()
        {
            lock (_idResetQueueLock)
            {
                _idResetQueue.TrimExcess();
            }
        }

        public void Update()
        {
            try
            {
                if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight))
                {
                    return;
                }
                if (HighLogic.LoadedSceneIsEditor)
                {
                    foreach (var activeStrut in Util.Util.GetAllActiveStruts())
                    {
                        activeStrut.OnUpdate();
                    }
                }
                if (HighLogic.LoadedSceneIsFlight)
                {
                    _processIdResets();
                }
                if (Config.Instance.ShowStraightOutHint && this._straightOutHintActiveParts != null)
                {
                    var remList = new List<StraightOutHintActivePart>();
                    var renewList = new List<StraightOutHintActivePart>();
                    foreach (var straightOutHintActivePart in this._straightOutHintActiveParts)
                    {
                        if (straightOutHintActivePart.HasToBeRemoved)
                        {
                            remList.Add(straightOutHintActivePart);
                        }
                        else
                        {
                            renewList.Add(straightOutHintActivePart);
                        }
                    }
                    foreach (var straightOutHintActivePart in remList)
                    {
                        this._straightOutHintActiveParts.Remove(straightOutHintActivePart);
                        Destroy(straightOutHintActivePart.HintObject);
                    }
                    foreach (var straightOutHintActivePart in renewList)
                    {
                        UpdateStraightOutHint(straightOutHintActivePart.Module, straightOutHintActivePart.HintObject);
                    }
                }
                var resetList = new List<HighlightedPart>();
                if (this._targetHighlightedParts != null)
                {
                    resetList = this._targetHighlightedParts.Where(targetHighlightedPart => targetHighlightedPart != null && targetHighlightedPart.HasToBeRemoved).ToList();
                }
                foreach (var targetHighlightedPart in resetList)
                {
                    targetHighlightedPart.Part.SetHighlightDefault();
                    try
                    {
                        if (this._targetHighlightedParts != null)
                        {
                            this._targetHighlightedParts.Remove(targetHighlightedPart);
                        }
                    }
                    catch (NullReferenceException)
                    {
                        //multithreading issue occured here, don't know if fixed
                    }
                }
                if (this._targetHighlightedParts != null)
                {
                    foreach (var targetHighlightedPart in this._targetHighlightedParts)
                    {
                        var part = targetHighlightedPart.Part;
                        part.SetHighlightColor(Color.cyan);
                        part.SetHighlight(true);
                    }
                }
                if (Mode == AddonMode.None || CurrentTargeter == null)
                {
                    if (this._resetAllHighlighting)
                    {
                        this._resetAllHighlighting = false;
                        foreach (var moduleActiveStrut in Util.Util.GetAllActiveStruts())
                        {
                            moduleActiveStrut.part.SetHighlightDefault();
                        }
                    }
                    _connector.SetActive(false);
                    return;
                }
                this._resetAllHighlighting = true;
                if (Mode == AddonMode.Link)
                {
                    _highlightCurrentTargets();
                }
                var mp = Util.Util.GetMouseWorldPosition();
                _pointToMousePosition(mp);
                var raycast = Util.Util.PerformRaycast(CurrentTargeter.Origin.position, mp, CurrentTargeter.Origin.right*-1);
                if (!raycast.HitResult)
                {
                    var handled = false;
                    if (Mode == AddonMode.Link && Input.GetKeyDown(KeyCode.Mouse0))
                    {
                        CurrentTargeter.AbortLink();
                        CurrentTargeter.UpdateGui();
                        handled = true;
                    }
                    if (Mode == AddonMode.FreeAttach && Input.GetKeyDown(KeyCode.X))
                    {
                        Mode = AddonMode.None;
                        CurrentTargeter.UpdateGui();
                        handled = true;
                    }
                    _connector.SetActive(false);
                    if (HighLogic.LoadedSceneIsEditor && handled)
                    {
                        Input.ResetInputAxes();
                        InputLockManager.RemoveControlLock(Config.Instance.EditorInputLockId);
                    }
                    return;
                }
                var validPos = _determineColor(mp, raycast);
                _processUserInput(mp, raycast, validPos);
            }
            catch (NullReferenceException)
            {
                /*
                 * For no apparent reason an exception is thrown on first load.
                 * I found no way to circumvent this.
                 * Since the exception has to be handled only once we are 
                 * "just" entering the try block constantly which I consider 
                 * still to be preferred over an unhandled exception.
                 */
            }
        }

        private static void UpdateStraightOutHint(ModuleActiveStrut module, GameObject hint)
        {
            hint.SetActive(false);
            var ray = new Ray(module.Origin.position, module.Origin.transform.right*-1);
            RaycastHit info;
            var maxDist = Config.Instance.MaxDistance;
            var raycast = Physics.Raycast(ray, out info, maxDist);
            var trans = hint.transform;
            trans.position = module.Origin.position;
            var dist = raycast
                           ? info.distance/2f
                           : maxDist;
            if (raycast)
            {
                trans.LookAt(info.point);
            }
            else
            {
                trans.LookAt(module.Origin.transform.position + module.Origin.transform.right*-1);
            }
            trans.Rotate(new Vector3(0, 1, 0), 90f);
            trans.Rotate(new Vector3(0, 0, 1), 90f);
            trans.localScale = new Vector3(0.05f, dist, 0.05f);
            trans.Translate(new Vector3(0f, dist, 0f));
            hint.SetActive(true);
            //print(string.Format("claculated dist = {0}", dist));
        }

        private static bool _checkForModule(Part part)
        {
            return part.Modules.Contains(Config.Instance.ModuleName);
        }

        private static bool _determineColor(Vector3 mp, RaycastResult raycast)
        {
            var validPosition = IsValidPosition(raycast);
            var mr = _connector.GetComponent<MeshRenderer>();
            mr.material.color =
                Util.Util.MakeColorTransparent(validPosition
                                                   ? (Mode == AddonMode.Link && !raycast.HittedPart.Modules.Contains(Config.Instance.ModuleName)) ||
                                                     (Mode == AddonMode.FreeAttach &&
                                                      (raycast.HittedPart.Modules.Contains(Config.Instance.ModuleName) ||
                                                       !raycast.HittedPart.Modules.Contains(Config.Instance.ModuleActiveStrutFreeAttachTarget)))
                                                         ? Color.yellow
                                                         : Color.green
                                                   : Color.red);
            if (Mode == AddonMode.FreeAttach)
            {
                validPosition = validPosition && !raycast.HittedPart.Modules.Contains(Config.Instance.ModuleName) && raycast.HittedPart.Modules.Contains(Config.Instance.ModuleActiveStrutFreeAttachTarget);
            }
            return validPosition;
        }

        private static void _highlightCurrentTargets()
        {
            var targets = Util.Util.GetAllActiveStruts().Where(m => m.Mode == Util.Mode.Target).Select(m => m.part).ToList();
            foreach (var part in targets)
            {
                part.SetHighlightColor(Color.green);
                part.SetHighlight(true);
            }
        }

        private static void _pointToMousePosition(Vector3 mp)
        {
            _connector.SetActive(true);
            var trans = _connector.transform;
            trans.position = CurrentTargeter.Origin.position;
            trans.LookAt(mp);
            trans.localScale = new Vector3(trans.position.x, trans.position.y, 1);
            var dist = Vector3.Distance(Vector3.zero, trans.InverseTransformPoint(mp))/2.0f;
            trans.localScale = new Vector3(0.05f, dist, 0.05f);
            trans.Rotate(new Vector3(0, 0, 1), 90f);
            trans.Rotate(new Vector3(1, 0, 0), 90f);
            trans.Translate(new Vector3(0f, dist, 0f));
        }

        private static void _processIdResets()
        {
            if (_idResetCounter > 0)
            {
                _idResetCounter--;
                return;
            }
            _idResetCounter = Config.IdResetCheckInterval;
            var updateFlag = false;
            while (!IsQueueEmpty())
            {
                var module = Dequeue();
                if (module != null)
                {
                    module.ResetId();
                }
                updateFlag = true;
            }
            if (updateFlag)
            {
                Debug.Log("[AS] IDs have been updated.");
            }
            if (_idResetTrimFlag)
            {
                TrimQueue();
            }
            else
            {
                _idResetTrimFlag = true;
            }
        }

        private static void _processUserInput(Vector3 mp, RaycastResult raycast, bool validPos)
        {
            var handled = false;
            switch (Mode)
            {
                case AddonMode.Link:
                {
                    if (Input.GetKeyDown(KeyCode.Mouse0))
                    {
                        if (validPos && raycast.HittedPart.Modules.Contains(Config.Instance.ModuleName))
                        {
                            var moduleActiveStrut = raycast.HittedPart.Modules[Config.Instance.ModuleName] as ModuleActiveStrut;
                            if (moduleActiveStrut != null)
                            {
                                moduleActiveStrut.SetAsTarget();
                                handled = true;
                            }
                        }
                    }
                    else if (Input.GetKeyDown(KeyCode.X))
                    {
                        CurrentTargeter.AbortLink();
                        handled = true;
                    }
                }
                    break;
                case AddonMode.FreeAttach:
                {
                    if (Input.GetKeyDown(KeyCode.Mouse0))
                    {
                        if (validPos)
                        {
                            CurrentTargeter.PlaceFreeAttach(raycast.HittedPart, raycast.Hit.point);
                            handled = true;
                        }
                    }
                    else if (Input.GetKeyDown(KeyCode.X))
                    {
                        Mode = AddonMode.None;
                        handled = true;
                    }
                }
                    break;
            }
            if (HighLogic.LoadedSceneIsEditor && handled)
            {
                Input.ResetInputAxes();
                InputLockManager.RemoveControlLock(Config.Instance.EditorInputLockId);
            }
        }
    }

    public class HighlightedPart
    {
        public bool HasToBeRemoved
        {
            get
            {
                var now = DateTime.Now;
                var dur = (now - this.HighlightStartTime).TotalSeconds;
                return dur >= this.Duration;
            }
        }

        public DateTime HighlightStartTime { get; set; }
        public Guid ModuleID { get; set; }
        public Part Part { get; set; }
        public int Duration { get; set; }

        public HighlightedPart(Part part, Guid moduleId)
        {
            this.Part = part;
            this.HighlightStartTime = DateTime.Now;
            this.ModuleID = moduleId;
            this.Duration = Config.Instance.TargetHighlightDuration;
        }
    }

    public class StraightOutHintActivePart : HighlightedPart
    {
        public GameObject HintObject { get; set; }
        public ModuleActiveStrut Module { get; set; }

        public StraightOutHintActivePart(Part part, Guid moduleId, GameObject hintObject, ModuleActiveStrut module) : base(part, moduleId)
        {
            this.HintObject = hintObject;
            this.Module = module;
            this.Duration = Config.Instance.StraightOutHintDuration;
        }
    }

    public enum AddonMode
    {
        FreeAttach,
        Link,
        None
    }
}