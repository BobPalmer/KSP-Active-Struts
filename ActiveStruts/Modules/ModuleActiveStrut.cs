﻿/*
The MIT License (MIT)
Copyright (c) 2014 marce

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System;
using System.Linq;
using System.Security.Cryptography;
using ActiveStruts.Addons;
using ActiveStruts.Util;
using UnityEngine;

namespace ActiveStruts.Modules
{
    public class ModuleActiveStrut : PartModule
    {
        private const ControlTypes EditorLockMask = ControlTypes.EDITOR_PAD_PICK_PLACE | ControlTypes.EDITOR_ICON_PICK;
        private readonly object _freeAttachStrutUpdateLock = new object();
        [KSPField(isPersistant = true)] public string FreeAttachPositionOffsetVector;
        [KSPField(isPersistant = true)] public bool FreeAttachPositionOffsetVectorSetInEditor = false;
        [KSPField(isPersistant = true)] public string FreeAttachTargetId = Guid.Empty.ToString();
        [KSPField(isPersistant = true)] public string Id = Guid.Empty.ToString();
        [KSPField(isPersistant = true)] public bool IsConnectionOrigin = false;
        [KSPField(isPersistant = true)] public bool IsFreeAttached = false;
        [KSPField(isPersistant = true)] public bool IsHalfWayExtended = false;
        [KSPField(isPersistant = true)] public bool IsLinked = false;
        [KSPField(isPersistant = true)] public bool IsOwnVesselConnected = false;
        [KSPField(isPersistant = true)] public bool IsTargetOnly = false;
        public ModuleActiveStrut OldTargeter;
        public Transform Origin;
        public FXGroup SoundAttach;
        public FXGroup SoundBreak;
        public FXGroup SoundDetach;
        [KSPField(isPersistant = false, guiActive = true)] public string State = "n.a.";
        [KSPField(guiActive = true)] public string Strength = LinkType.None.ToString();
        public Transform Strut;
        [KSPField(isPersistant = true)] public string StrutName = "strut";
        [KSPField(isPersistant = true)] public string TargetId = Guid.NewGuid().ToString();
        [KSPField(isPersistant = true)] public string TargeterId = Guid.NewGuid().ToString();
        private bool _delayedStartFlag;
        private Part _freeAttachPart;
        private ModuleActiveStrutFreeAttachTarget _freeAttachTarget;
        private ConfigurableJoint _joint;
        private AttachNode _jointAttachNode;
        private bool _jointBroken;
        private LinkType _linkType;
        private Mode _mode = Mode.Undefined;
        private PartJoint _partJoint;
        private bool _soundFlag;
        private int _strutRealignCounter;
        private int _ticksForDelayedStart;
        private object _jointBreakLock;
        [KSPField(isPersistant = true)] public bool IsDocked;
        [KSPField(isPersistant = true)] public string DockingVesselName;
        [KSPField(isPersistant = true)] public uint DockingVesselId;
        [KSPField(isPersistant = true)] public string DockingVesselTypeString;

        private Part FreeAttachPart
        {
            get
            {
                if (this._freeAttachPart != null)
                {
                    return this._freeAttachPart;
                }
                var rayRes = this.CheckFreeAttachPoint();
                if (rayRes.HitResult)
                {
                    this._freeAttachPart = rayRes.TargetPart;
                }
                return this._freeAttachPart;
            }
        }

        public Vector3 FreeAttachPositionOffset
        {
            get
            {
                if (this.FreeAttachPositionOffsetVector == null)
                {
                    return Vector3.zero;
                }
                var vArr = this.FreeAttachPositionOffsetVector.Split(' ').Select(Convert.ToSingle).ToArray();
                return new Vector3(vArr[0], vArr[1], vArr[2]);
            }
            set { this.FreeAttachPositionOffsetVector = String.Format("{0} {1} {2}", value.x, value.y, value.z); }
        }

        public ModuleActiveStrutFreeAttachTarget FreeAttachTarget
        {
            get { return this._freeAttachTarget ?? (this._freeAttachTarget = Util.Util.FindFreeAttachTarget(new Guid(this.FreeAttachTargetId))); }
            set
            {
                this.FreeAttachTargetId = value != null ? value.ID.ToString() : Guid.Empty.ToString();
                this._freeAttachTarget = value;
            }
        }

        public Guid ID
        {
            get
            {
                if (this.Id == null || new Guid(this.Id) == Guid.Empty)
                {
                    this.Id = Guid.NewGuid().ToString();
                }
                return new Guid(this.Id);
            }
        }

        public bool IsConnectionFree
        {
            get { return this.IsTargetOnly || !this.IsLinked || (this.IsLinked && this.Mode == Mode.Unlinked); }
        }

        public LinkType LinkType
        {
            get { return this._linkType; }
            set
            {
                this._linkType = value;
                this.Strength = value.ToString();
            }
        }

        public Mode Mode
        {
            get { return this._mode; }
            set
            {
                this._mode = value;
                this.State = value.ToString();
            }
        }

        public ModuleActiveStrut Target
        {
            get { return this.TargetId == Guid.Empty.ToString() ? null : Util.Util.GetStrutById(new Guid(this.TargetId)); }
            set { this.TargetId = value != null ? value.ID.ToString() : Guid.Empty.ToString(); }
        }

        public ModuleActiveStrut Targeter
        {
            get { return this.TargeterId == Guid.Empty.ToString() ? null : Util.Util.GetStrutById(new Guid(this.TargeterId)); }
            set { this.TargeterId = value != null ? value.ID.ToString() : Guid.Empty.ToString(); }
        }

        [KSPEvent(name = "AbortLink", active = false, guiName = "Abort Link", guiActiveEditor = true, guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void AbortLink()
        {
            this.Mode = Mode.Unlinked;
            Util.Util.ResetAllFromTargeting();
            ActiveStrutsAddon.Mode = AddonMode.None;
            OSD.Info("Link aborted.");
            this.UpdateGui();
        }

        public void CreateJoint(Rigidbody originBody, Rigidbody targetBody, LinkType type, Vector3 anchorPosition)
        {
            var breakForce = type.GetJointStrength();
            this._joint = originBody.gameObject.AddComponent<ConfigurableJoint>();
            //this._joint = part.attachJoint.Joint.rigidbody.gameObject.AddComponent<ConfigurableJoint>();
            this._joint.connectedBody = targetBody;
            this._joint.breakForce = this._joint.breakTorque = breakForce;
            this._joint.xMotion = ConfigurableJointMotion.Locked;
            this._joint.yMotion = ConfigurableJointMotion.Locked;
            this._joint.zMotion = ConfigurableJointMotion.Locked;
            this._joint.angularXMotion = ConfigurableJointMotion.Locked;
            this._joint.angularYMotion = ConfigurableJointMotion.Locked;
            this._joint.angularZMotion = ConfigurableJointMotion.Locked;
            this._joint.projectionAngle = 0f;
            this._joint.projectionDistance = 0f;
            this._joint.anchor = anchorPosition;
            this.LinkType = type;
            if (!this.IsFreeAttached)
            {
                this.Target.LinkType = type;
                this.IsOwnVesselConnected = this.Target.vessel == this.vessel;
            }
            else
            {
                this.IsOwnVesselConnected = this.FreeAttachPart.vessel == this.vessel;
            }
            this._manageAttachNode(breakForce);
            Debug.Log("[AS] created a joint with strength: " + breakForce);
            this.PlayAttachSound();
        }

        public void CreateStrut(Vector3 target, float distancePercent = 1)
        {
            var strut = this.Strut;
            strut.LookAt(target);
            strut.localScale = new Vector3(1, 1, 1);
            var distance = -1*Vector3.Distance(Vector3.zero, this.Strut.InverseTransformPoint(target))*distancePercent;
            if (this.IsFreeAttached)
            {
                distance -= Config.Instance.FreeAttachStrutExtension;
            }
            this.Strut.localScale = new Vector3(1, 1, distance);
        }

        public void DestroyJoint()
        {
            try
            {
                if (this._partJoint != null)
                {
                    this._partJoint.DestroyJoint();
                    this.part.attachNodes.Remove(this._jointAttachNode);
                    this._jointAttachNode.owner = null;
                }
                DestroyImmediate(this._partJoint);
            }
            catch (Exception)
            {
                //try
                //{
                //    Destroy(this._joint);
                //    Destroy(this._partJoint);
                //}
                //catch (Exception)
                //{
                //    //nothing to destroy
                //}
            }
            DestroyImmediate(this._joint);
            this._partJoint = null;
            this._jointAttachNode = null;
            this._joint = null;
            this.LinkType = LinkType.None;
            if (this.IsDocked)
            {
                this.ProcessUnDock(true);
            }
        }

        private void ProcessUnDock(bool undockByUnlink = false)
        {
            if (HighLogic.LoadedSceneIsEditor || (!this.IsLinked && !undockByUnlink) || !this.IsConnectionOrigin || this.IsTargetOnly || this.IsOwnVesselConnected || (this.IsFreeAttached ? FreeAttachPart == null : Target == null) ||
                !this.IsDocked)
            {
                OSD.Warn("Can't undock.");
                return;
            }
            var vi = new DockedVesselInfo
                     {
                         name = this.DockingVesselName,
                         rootPartUId = this.DockingVesselId,
                         vesselType = (VesselType) Enum.Parse(typeof(VesselType), this.DockingVesselTypeString)
                     };
            this.IsDocked = false;
            if (this.IsFreeAttached)
            {
                this.FreeAttachPart.Undock(vi);
            }
            else
            {
                this.Target.part.Undock(vi);
            }
            this.UpdateGui();
        }

        public void DestroyStrut()
        {
            this.Strut.localScale = Vector3.zero;
        }

        [KSPEvent(name = "FreeAttach", active = false, guiActiveEditor = false, guiName = "FreeAttach Link", guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void FreeAttach()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                InputLockManager.SetControlLock(EditorLockMask, Config.Instance.EditorInputLockId);
            }
            OSD.Info(Config.Instance.FreeAttachHelpText);
            ActiveStrutsAddon.CurrentTargeter = this;
            ActiveStrutsAddon.Mode = AddonMode.FreeAttach;
        }

        [KSPEvent(name = "ResetId", active = false, guiActiveEditor = false, guiName = "Reset ID", guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void ResetId()
        {
            if (!this.IsLinked && !this.IsDocked)
            {
                var oldId = this.ID.ToString();
                this.Id = Guid.NewGuid().ToString();
                foreach (var moduleActiveStrut in Util.Util.GetAllActiveStruts())
                {
                    if (moduleActiveStrut.TargetId != null && moduleActiveStrut.TargetId == oldId)
                    {
                        moduleActiveStrut.TargetId = this.Id;
                    }
                    if (moduleActiveStrut.TargeterId != null && moduleActiveStrut.TargeterId == oldId)
                    {
                        moduleActiveStrut.TargeterId = this.Id;
                    }
                }
                //if (this.Targeter != null && this.Targeter.TargetId == oldId)
                //{
                //    this.Targeter.TargetId = this.Id;
                //}
                //if (this.Target != null && this.Target.TargeterId == oldId)
                //{
                //    this.Target.TargeterId = this.Id;
                //}
                //if (this.IsTargetOnly)
                //{
                //    foreach (var connectedTargeter in this.GetAllConnectedTargeters())
                //    {
                //        connectedTargeter.TargetId = this.Id;
                //    }
                //}
                OSD.Info("New ID created and set. Bloody workaround...");
            }
        }

        [KSPEvent(name = "FreeAttachStraight", active = false, guiName = "Straight Up FreeAttach", guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void FreeAttachStraight()
        {
            var ray = new Ray(this.Origin.position, this.Origin.transform.right);
            RaycastHit info;
            var raycast = Physics.Raycast(ray, out info, Config.Instance.MaxDistance);
            if (raycast)
            {
                var hittedPart = info.PartFromHit();
                var valid = hittedPart != null;
                if (HighLogic.LoadedSceneIsFlight && valid)
                {
                    valid = hittedPart.vessel == this.vessel;
                }
                if (valid)
                {
                    this.PlaceFreeAttach(hittedPart, info.point);
                }
            }
            else
            {
                OSD.Warn("Nothing has been hit.");
            }
        }

        [KSPAction("FreeAttachStraightAction", KSPActionGroup.None, guiName = "Straight FreeAttach")]
        public void FreeAttachStraightAction(KSPActionParam param)
        {
            if (this.Mode == Mode.Unlinked && !this.IsTargetOnly)
            {
                this.FreeAttachStraight();
            }
        }

        [KSPEvent(name = "Link", active = false, guiName = "Link", guiActiveEditor = false, guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void Link()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                InputLockManager.SetControlLock(EditorLockMask, Config.Instance.EditorInputLockId);
            }
            this.Mode = Mode.Targeting;
            foreach (var possibleTarget in this.GetAllPossibleTargets())
            {
                possibleTarget.SetTargetedBy(this);
                possibleTarget.UpdateGui();
            }
            ActiveStrutsAddon.Mode = AddonMode.Link;
            ActiveStrutsAddon.CurrentTargeter = this;
            OSD.Info(Config.Instance.LinkHelpText, 5);
            this.UpdateGui();
        }

        public void OnJointBreak(float breakForce)
        {
            //Destroy(this._partJoint);
            //Destroy(this._joint);
            try
            {
                this._partJoint.DestroyJoint();
                this.part.attachNodes.Remove(this._jointAttachNode);
                this._jointAttachNode.owner = null;
            }
            catch (NullReferenceException)
            {
                //already destroyed
            }
            //lock (_jointBreakLock)
            //{
            //this.Unlink();
            //if (this._jointBroken)
            //{
            //    return;
            //}
            this._jointBroken = true;
            var strength = this.LinkType.GetJointStrength();
            var diff = breakForce - strength;
            //this.DestroyJoint();
            this.PlayBreakSound();
            OSD.Warn("Joint broken! Applied force was " + breakForce.ToString("R") + " while the joint could only take " + strength.ToString("R") + " (difference: " + diff.ToString("R") + ")", 5);
            //}
        }

        //public override void OnFixedUpdate()
        //{
        //}

        public override void OnStart(StartState state)
        {
            _jointBreakLock = new object();
            if (!this.IsTargetOnly)
            {
                this.Strut = this.part.FindModelTransform(this.StrutName);
                DestroyImmediate(this.Strut.collider);
                this.DestroyStrut();
            }
            if (HighLogic.LoadedSceneIsEditor)
            {
                this.part.OnEditorAttach += this.ProcessOnPartCopy;
                //this.part.OnEditorDetach += this.HandleEditorDetach;
                //Debug.Log("[AS] registered editor events");
            }
            this.Origin = this.part.transform;
            this._delayedStartFlag = true;
            this._ticksForDelayedStart = HighLogic.LoadedSceneIsEditor ? 0 : Config.Instance.StartDelay;
            this._strutRealignCounter = Config.Instance.StrutRealignInterval*(HighLogic.LoadedSceneIsEditor ? 3 : 0);
            if (this.SoundAttach == null || this.SoundBreak == null || this.SoundDetach == null ||
                !GameDatabase.Instance.ExistsAudioClip(Config.Instance.SoundAttachFileUrl) ||
                !GameDatabase.Instance.ExistsAudioClip(Config.Instance.SoundDetachFileUrl) ||
                !GameDatabase.Instance.ExistsAudioClip(Config.Instance.SoundBreakFileUrl))
            {
                Debug.Log("[AS] sounds cannot be loaded." + (this.SoundAttach == null ? "FXGroup not instantiated" : "sound file not found"));
                this._soundFlag = false;
            }
            else
            {
                SetupFXGroup(this.SoundAttach, this.gameObject, Config.Instance.SoundAttachFileUrl);
                SetupFXGroup(this.SoundDetach, this.gameObject, Config.Instance.SoundDetachFileUrl);
                SetupFXGroup(this.SoundBreak, this.gameObject, Config.Instance.SoundBreakFileUrl);
                this._soundFlag = true;
            }
        }

        //public void HandleEditorDetach()
        //{
        //    Debug.Log("[AS] I have been detached");
        //}

        // ReSharper disable once InconsistentNaming

        public override void OnUpdate()
        {
            if (this._delayedStartFlag)
            {
                this._delayedStart();
                return;
            }
            if (this._jointBroken)
            {
                this._jointBroken = false;
                this.Unlink();
                return;
            }
            if (this.IsLinked)
            {
                if (this._strutRealignCounter > 0)
                {
                    this._strutRealignCounter--;
                }
                else
                {
                    this._strutRealignCounter = Config.Instance.StrutRealignInterval;
                    this._realignStrut();
                    this.LinkType = this.IsFreeAttached ? LinkType.Weak : this.IsConnectionOrigin ? this.Target.IsTargetOnly ? LinkType.Normal : LinkType.Maximum : this.IsTargetOnly ? LinkType.Normal : LinkType.Maximum;
                }
            }
            else
            {
                this.LinkType = LinkType.None;
            }
            if (this.Mode == Mode.Unlinked || this.Mode == Mode.Target || this.Mode == Mode.Targeting)
            {
                return;
            }
            if (this.IsTargetOnly)
            {
                if (!this.AnyTargetersConnected())
                {
                    this.Mode = Mode.Unlinked;
                    this.UpdateGui();
                    return;
                }
            }
            if (this.Mode == Mode.Linked)
            {
                if (HighLogic.LoadedSceneIsEditor)
                {
                    return;
                }
                if (this.IsOwnVesselConnected)
                {
                    //Debug.Log("[AS] unlink if not currvessel");
                    if (this.IsFreeAttached)
                    {
                        if (this.FreeAttachPart != null)
                        {
                            if (this.FreeAttachPart.vessel != this.vessel)
                            {
                                this.IsOwnVesselConnected = false;
                            }
                        }
                    }
                    else if (this.IsTargetOnly)
                    {
                        foreach (var connectedTargeter in this.GetAllConnectedTargeters().Where(connectedTargeter => connectedTargeter.vessel != null && connectedTargeter.vessel != this.vessel))
                        {
                            connectedTargeter.Unlink();
                        }
                    }
                    else if (this.Target != null)
                    {
                        if (this.Target.vessel != this.vessel)
                        {
                            this.IsOwnVesselConnected = false;
                        }
                    }
                    if (!this.IsOwnVesselConnected)
                    {
                        this.Unlink();
                    }
                    this.UpdateGui();
                }
                //TODO decouple and staging handling
                //_manageAttachNode();
                //if (this.IsFreeAttached)
                //{
                //    if (this.FreeAttachPart != null && (HighLogic.LoadedSceneIsEditor || this.FreeAttachPart.vessel == this.vessel))
                //    {
                //        return;
                //    }
                //    this.Unlink();
                //    return;
                //}
                //if (this.IsConnectionOrigin)
                //{
                //    if (this.Target != null && (HighLogic.LoadedSceneIsEditor || this.Target.vessel == this.vessel))
                //    {
                //        return;
                //    }
                //    this.DestroyJoint();
                //    this.DestroyStrut();
                //    this.Mode = Mode.Unlinked;
                //}
                //else
                //{
                //    if (this.Targeter != null && (HighLogic.LoadedSceneIsEditor || this.Targeter.vessel == this.vessel))
                //    {
                //        return;
                //    }
                //    this.DestroyStrut();
                //    this.Mode = Mode.Unlinked;
                //}                
            }
        }

        public void PlaceFreeAttach(Part hittedPart, Vector3 hitPosition, bool overridePositionOffset = true)
        {
            lock (this._freeAttachStrutUpdateLock)
            {
                ActiveStrutsAddon.Mode = AddonMode.None;
                if (overridePositionOffset)
                {
                    this.FreeAttachPositionOffset = hitPosition - hittedPart.transform.position; //hittedPart.transform.position;
                    if (HighLogic.LoadedSceneIsEditor)
                    {
                        this.FreeAttachPositionOffset = hittedPart.transform.rotation.Inverse()*this.FreeAttachPositionOffset;
                        this.FreeAttachPositionOffsetVectorSetInEditor = true;
                    }
                    else
                    {
                        this.FreeAttachPositionOffsetVectorSetInEditor = false;
                    }
                }
                var target = hittedPart.Modules[Config.Instance.ModuleActiveStrutFreeAttachTarget] as ModuleActiveStrutFreeAttachTarget;
                if (target != null)
                {
                    this.FreeAttachTarget = target;
                }
                this._freeAttachPart = hittedPart;
                this.Mode = Mode.Linked;
                this.IsLinked = true;
                this.IsFreeAttached = true;
                this.IsConnectionOrigin = true;
                this.DestroyJoint();
                this.DestroyStrut();
                if (target != null)
                {
                    this.CreateJoint(this.part.rigidbody, target.PartRigidbody, LinkType.Weak, (hitPosition + this.Origin.position)/2);
                }
                var targetPoint = this._convertFreeAttachRayHitPointToStrutTarget();
                //Debug.Log("[AS] target point: " + targetPoint);
                this.CreateStrut(targetPoint);
                this.Target = null;
                this.Targeter = null;
                OSD.Success("FreeAttach Link established!");
            }
            this.UpdateGui();
        }

        public void PlayAttachSound()
        {
            this.PlayAudio(this.SoundAttach);
        }

        private void PlayAudio(FXGroup group)
        {
            if (!this._soundFlag || group == null || group.audio == null)
            {
                return;
            }
            group.audio.Play();
        }

        public void PlayBreakSound()
        {
            this.PlayAudio(this.SoundBreak);
        }

        public void PlayDetachSound()
        {
            this.PlayAudio(this.SoundDetach);
        }

        public void ProcessOnPartCopy()
        {
            var allModules = Util.Util.GetAllActiveStruts();
            if (allModules != null && allModules.Any(m => m.ID == this.ID))
            {
                this.ResetActiveStrutToDefault();
            }
            else
            {
                this.Unlink();
                this.OnUpdate();
            }
        }

        public void ProcessUnlink(bool secondary = false)
        {
            if (!this.IsTargetOnly && (this.Target != null || this.Targeter != null))
            {
                if (!this.IsConnectionOrigin && !secondary && this.Targeter != null)
                {
                    try
                    {
                        this.Targeter.Unlink();
                    }
                    catch (NullReferenceException)
                    {
                        //fail silently
                    }
                    return;
                }
                if (this.IsFreeAttached)
                {
                    this.IsFreeAttached = false;
                }
                this.Mode = Mode.Unlinked;
                this.IsLinked = false;
                this.DestroyJoint();
                this.DestroyStrut();
                this.LinkType = LinkType.None;
                if (this.IsConnectionOrigin)
                {
                    if (!this.IsFreeAttached)
                    {
                        if (this.Target != null)
                        {
                            try
                            {
                                this.Target.ProcessUnlink(true);
                                if (HighLogic.LoadedSceneIsEditor)
                                {
                                    this.Target.Targeter = null;
                                    this.Target = null;
                                }
                                //this.Target.UpdateGui();
                            }
                            catch (NullReferenceException)
                            {
                                //fail silently
                            }
                        }
                    }
                    OSD.Success("Unlinked!");
                    this.PlayDetachSound();
                }
                this.IsConnectionOrigin = false;
                this.UpdateGui();
                return;
            }
            if (this.IsTargetOnly)
            {
                if (!this.AnyTargetersConnected())
                {
                    this.Mode = Mode.Unlinked;
                    this.IsLinked = false;
                }
                this.UpdateGui();
                return;
            }
            if (this.IsFreeAttached)
            {
                this.IsFreeAttached = false;
            }
            this.FreeAttachTarget = null;
            this.Mode = Mode.Unlinked;
            this.IsLinked = false;
            this.DestroyStrut();
            this.DestroyJoint();
            this.LinkType = LinkType.None;
            this.UpdateGui();
            this.PlayDetachSound();
        }

        private void Reconnect()
        {
            if (this.IsFreeAttached)
            {
                if (this.FreeAttachTarget != null)
                {
                    var rayRes = Util.Util.PerformRaycast(this.Origin.position, this.FreeAttachTarget.PartOrigin.position, this.Origin.right);
                    if (rayRes.HittedPart != null && rayRes.DistanceFromOrigin <= Config.Instance.MaxDistance)
                    {
                        this.PlaceFreeAttach(rayRes.HittedPart, rayRes.Hit.point, false);
                        this.UpdateGui();
                        return;
                    }
                }
                this.IsFreeAttached = false;
                this.Mode = Mode.Unlinked;
                this.IsConnectionOrigin = false;
                this.LinkType = LinkType.None;
                this.UpdateGui();
                return;
            }
            if (this.IsConnectionOrigin)
            {
                //Debug.Log("[AS] reconnecting targeter");
                if (this.Target != null && this.IsPossibleTarget(this.Target))
                {
                    if (!this.Target.IsTargetOnly)
                    {
                        this.CreateStrut(this.Target.Origin.position, 0.5f);
                    }
                    else
                    {
                        this.CreateStrut(this.Target.Origin.position);
                    }
                    this.CreateJoint(this.part.rigidbody, this.Target.part.rigidbody, this.Target.IsTargetOnly ? LinkType.Normal : LinkType.Maximum, this.Target.transform.position);
                    this.Mode = Mode.Linked;
                    this.Target.Mode = Mode.Linked;
                    this.IsLinked = true;
                    //Debug.Log("[AS] reconnected targeter");
                }
            }
            else
            {
                if (this.IsTargetOnly)
                {
                    this.Mode = Mode.Linked;
                    this.IsLinked = true;
                }
                else if (this.IsPossibleTarget(this.Targeter))
                {
                    this.CreateStrut(this.Targeter.Origin.position, 0.5f);
                    this.Mode = Mode.Linked;
                    this.IsLinked = true;
                    this.LinkType = LinkType.Maximum;
                }
            }
            this.UpdateGui();
        }

        private void ResetActiveStrutToDefault()
        {
            this.Target = null;
            this.Targeter = null;
            this.IsConnectionOrigin = false;
            this.IsFreeAttached = false;
            this.Mode = Mode.Unlinked;
            this.IsHalfWayExtended = false;
            this.Id = Guid.NewGuid().ToString();
            this.LinkType = LinkType.None;
            this.OldTargeter = null;
            this.FreeAttachTarget = null;
            this.IsFreeAttached = false;
            this.IsLinked = false;
            if (!this.IsTargetOnly)
            {
                this.DestroyJoint();
                this.DestroyStrut();
            }
        }

        [KSPEvent(name = "SetAsTarget", active = false, guiName = "Set as Target", guiActiveEditor = false, guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void SetAsTarget()
        {
            this.IsLinked = true;
            this.part.SetHighlightDefault();
            this.Mode = Mode.Linked;
            this.IsConnectionOrigin = false;
            this.IsFreeAttached = false;
            if (!this.IsTargetOnly)
            {
                this.CreateStrut(this.Targeter.Origin.position, 0.5f);
            }
            this.Targeter.SetTarget(this);
            this.UpdateGui();
        }

        public void SetTarget(ModuleActiveStrut target)
        {
            this.Target = target;
            this.Mode = Mode.Linked;
            this.IsLinked = true;
            this.IsConnectionOrigin = true;
            this.CreateJoint(this.part.rigidbody, target.part.rigidbody, target.IsTargetOnly ? LinkType.Normal : LinkType.Maximum, this.Target.transform.position);
            this.CreateStrut(target.Origin.position, target.IsTargetOnly ? 1 : 0.5f);
            Util.Util.ResetAllFromTargeting();
            OSD.Success("Link established!");
            ActiveStrutsAddon.Mode = AddonMode.None;
            this.UpdateGui();
        }

        public void SetTargetedBy(ModuleActiveStrut targeter)
        {
            this.OldTargeter = this.Targeter;
            this.Targeter = targeter;
            this.Mode = Mode.Target;
        }

        private static void SetupFXGroup(FXGroup group, GameObject gameObject, string audioFileUrl)
        {
            group.audio = gameObject.AddComponent<AudioSource>();
            group.audio.clip = GameDatabase.Instance.GetAudioClip(audioFileUrl);
            group.audio.dopplerLevel = 0f;
            group.audio.rolloffMode = AudioRolloffMode.Linear;
            group.audio.maxDistance = 30f;
            group.audio.loop = false;
            group.audio.playOnAwake = false;
            group.audio.volume = GameSettings.SHIP_VOLUME;
        }

        [KSPEvent(name = "ToggleLink", active = false, guiName = "Toggle Link", guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void ToggleLink()
        {
            if (this.Mode == Mode.Linked)
            {
                if (this.IsConnectionOrigin)
                {
                    this.Unlink();
                }
                else
                {
                    if (this.Targeter != null)
                    {
                        this.Targeter.Unlink();
                    }
                }
            }
            else if (this.Mode == Mode.Unlinked && ((this.Target != null && this.Target.IsConnectionFree) || (this.Targeter != null && this.Targeter.IsConnectionFree)))
            {
                if (this.Target != null)
                {
                    if (this.IsPossibleTarget(this.Target))
                    {
                        this.Target.Targeter = this;
                        this.Target.SetAsTarget();
                    }
                    else
                    {
                        OSD.Warn("Can't relink at the moment, target may be obstructed.");
                    }
                }
                else if (this.Targeter != null)
                {
                    if (this.Targeter.IsPossibleTarget(this))
                    {
                        this.SetAsTarget();
                    }
                    else
                    {
                        OSD.Warn("Can't relink at the moment, targeter may be obstructed.");
                    }
                }
            }
            this.UpdateGui();
        }

        [KSPAction("ToggleLinkAction", KSPActionGroup.None, guiName = "Toggle Link")]
        public void ToggleLinkAction(KSPActionParam param)
        {
            if (this.Mode == Mode.Linked || (this.Mode == Mode.Unlinked && ((this.Target != null && this.Target.IsConnectionFree) || (this.Targeter != null && this.Targeter.IsConnectionFree))))
            {
                this.ToggleLink();
            }
        }

        [KSPEvent(name = "Unlink", active = false, guiName = "Unlink", guiActiveEditor = false, guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void Unlink()
        {
            this.ProcessUnlink();
        }

        public void UpdateGui()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (FlightGlobals.ActiveVessel.isEVA)
                {
                    this.Events["UnDock"].active = this.Events["UnDock"].guiActive = false;
                    this.Events["Dock"].active = this.Events["UnDock"].guiActive = false;
                    this.Events["Link"].active = this.Events["Link"].guiActive = false;
                    this.Events["AbortLink"].active = this.Events["AbortLink"].guiActive = false;
                    this.Events["ToggleLink"].active = this.Events["ToggleLink"].guiActive = false;
                    this.Events["FreeAttach"].active = this.Events["FreeAttach"].guiActive = false;
                    this.Events["SetAsTarget"].active = this.Events["SetAsTarget"].guiActive = false;
                    this.Events["FreeAttachStraight"].active = this.Events["FreeAttachStraight"].guiActive = false;
                    return;
                }
                switch (this.Mode)
                {
                    case Mode.Linked:
                    {
                        this.Events["Link"].active = this.Events["Link"].guiActive = false;
                        this.Events["SetAsTarget"].active = this.Events["SetAsTarget"].guiActive = false;
                        this.Events["FreeAttach"].active = this.Events["FreeAttach"].guiActive = false;
                        if (!this.IsTargetOnly)
                        {
                            this.Events["AbortLink"].active = this.Events["AbortLink"].guiActive = false;
                            if (this.IsFreeAttached)
                            {
                                this.Events["ToggleLink"].active = this.Events["ToggleLink"].guiActive = false;
                                this.Events["Unlink"].active = this.Events["Unlink"].guiActive = true;
                            }
                            else
                            {
                                this.Events["ToggleLink"].active = this.Events["ToggleLink"].guiActive = true;
                                this.Events["Unlink"].active = this.Events["Unlink"].guiActive = false;
                            }
                            if (!this.IsOwnVesselConnected && !this.IsDocked)
                            {
                                this.Events["Dock"].active = this.Events["Dock"].guiActive = true;
                                this.Events["UnDock"].active = this.Events["UnDock"].guiActive = false;
                            }
                            if (!this.IsOwnVesselConnected && this.IsDocked)
                            {
                                this.Events["Dock"].active = this.Events["Dock"].guiActive = false;
                                this.Events["UnDock"].active = this.Events["UnDock"].guiActive = true;
                            }
                        }
                        else
                        {
                            this.Events["Unlink"].active = this.Events["Unlink"].guiActive = false;
                            this.Events["ToggleLink"].active = this.Events["ToggleLink"].guiActive = false;
                        }
                    }
                        break;
                    case Mode.Unlinked:
                    {
                        this.Events["SetAsTarget"].active = this.Events["SetAsTarget"].guiActive = false;
                        this.Events["Unlink"].active = this.Events["Unlink"].guiActive = false;
                        this.Events["UnDock"].active = this.Events["UnDock"].guiActive = false;
                        this.Events["Dock"].active = this.Events["Dock"].guiActive = false;
                        if (this.IsTargetOnly)
                        {
                            this.Events["Link"].active = this.Events["Link"].guiActive = false;
                        }
                        else
                        {
                            this.Events["Link"].active = this.Events["Link"].guiActive = true;
                            this.Events["FreeAttach"].active = this.Events["FreeAttach"].guiActive = true;
                            this.Events["AbortLink"].active = this.Events["AbortLink"].guiActive = false;
                            if ((this.Target != null && this.Target.IsConnectionFree) || (this.Targeter != null && this.Targeter.IsConnectionFree))
                            {
                                this.Events["ToggleLink"].active = this.Events["ToggleLink"].guiActive = true;
                            }
                            else
                            {
                                this.Events["ToggleLink"].active = this.Events["ToggleLink"].guiActive = false;
                            }
                        }
                    }
                        break;
                    case Mode.Target:
                    {
                        this.Events["UnDock"].active = this.Events["UnDock"].guiActive = false;
                        this.Events["Dock"].active = this.Events["UnDock"].guiActive = false;
                        this.Events["SetAsTarget"].active = this.Events["SetAsTarget"].guiActive = true;
                        if (!this.IsTargetOnly)
                        {
                            this.Events["Link"].active = this.Events["Link"].guiActive = false;
                        }
                        this.Events["ToggleLink"].active = this.Events["ToggleLink"].guiActive = false;
                        this.Events["FreeAttach"].active = this.Events["FreeAttach"].guiActive = false;
                    }
                        break;
                    case Mode.Targeting:
                    {
                        this.Events["UnDock"].active = this.Events["UnDock"].guiActive = false;
                        this.Events["Dock"].active = this.Events["UnDock"].guiActive = false;
                        this.Events["Link"].active = this.Events["Link"].guiActive = false;
                        this.Events["AbortLink"].active = this.Events["AbortLink"].guiActive = true;
                        this.Events["ToggleLink"].active = this.Events["ToggleLink"].guiActive = false;
                        this.Events["FreeAttach"].active = this.Events["FreeAttach"].guiActive = false;
                    }
                        break;
                }
                this.Events["FreeAttachStraight"].active = this.Events["FreeAttachStraight"].guiActive = this.Events["FreeAttach"].active;
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                this.Events["ToggleLink"].active = this.Events["ToggleLink"].guiActive = this.Events["ToggleLink"].guiActiveEditor = false;
                this.Events["UnDock"].active = this.Events["UnDock"].guiActive = this.Events["UnDock"].guiActiveEditor = false;
                this.Events["Dock"].active = this.Events["Dock"].guiActive = this.Events["Dock"].guiActiveEditor = false;
                switch (this.Mode)
                {
                    case Mode.Linked:
                    {
                        if (!this.IsTargetOnly)
                        {
                            this.Events["Unlink"].active = this.Events["Unlink"].guiActive = this.Events["Unlink"].guiActiveEditor = true;
                        }
                        this.Events["Link"].active = this.Events["Link"].guiActive = this.Events["Link"].guiActiveEditor = false;
                        this.Events["SetAsTarget"].active = this.Events["SetAsTarget"].guiActive = this.Events["SetAsTarget"].guiActiveEditor = false;
                        this.Events["AbortLink"].active = this.Events["AbortLink"].guiActive = this.Events["AbortLink"].guiActiveEditor = false;
                        this.Events["FreeAttach"].active = this.Events["FreeAttach"].guiActive = this.Events["FreeAttach"].guiActiveEditor = false;
                    }
                        break;
                    case Mode.Unlinked:
                    {
                        this.Events["Unlink"].active = this.Events["Unlink"].guiActive = this.Events["Unlink"].guiActiveEditor = false;
                        this.Events["SetAsTarget"].active = this.Events["SetAsTarget"].guiActive = this.Events["SetAsTarget"].guiActiveEditor = false;
                        this.Events["AbortLink"].active = this.Events["AbortLink"].guiActive = this.Events["AbortLink"].guiActiveEditor = false;
                        if (!this.IsTargetOnly)
                        {
                            this.Events["Link"].active = this.Events["Link"].guiActive = this.Events["Link"].guiActiveEditor = true;
                            this.Events["FreeAttach"].active = this.Events["FreeAttach"].guiActive = this.Events["FreeAttach"].guiActiveEditor = true;
                        }
                    }
                        break;
                    case Mode.Target:
                    {
                        this.Events["Unlink"].active = this.Events["Unlink"].guiActive = this.Events["Unlink"].guiActiveEditor = false;
                        this.Events["Link"].active = this.Events["Link"].guiActive = this.Events["Link"].guiActiveEditor = false;
                        this.Events["SetAsTarget"].active = this.Events["SetAsTarget"].guiActive = this.Events["SetAsTarget"].guiActiveEditor = true;
                        this.Events["AbortLink"].active = this.Events["AbortLink"].guiActive = this.Events["AbortLink"].guiActiveEditor = false;
                        this.Events["FreeAttach"].active = this.Events["FreeAttach"].guiActive = this.Events["FreeAttach"].guiActiveEditor = false;
                    }
                        break;
                    case Mode.Targeting:
                    {
                        this.Events["Unlink"].active = this.Events["Unlink"].guiActive = this.Events["Unlink"].guiActiveEditor = false;
                        this.Events["Link"].active = this.Events["Link"].guiActive = this.Events["Link"].guiActiveEditor = false;
                        this.Events["SetAsTarget"].active = this.Events["SetAsTarget"].guiActive = this.Events["SetAsTarget"].guiActiveEditor = false;
                        this.Events["AbortLink"].active = this.Events["AbortLink"].guiActive = this.Events["AbortLink"].guiActiveEditor = true;
                        this.Events["FreeAttach"].active = this.Events["FreeAttach"].guiActive = this.Events["FreeAttach"].guiActiveEditor = false;
                    }
                        break;
                }               
                this.Events["FreeAttachStraight"].active = this.Events["FreeAttachStraight"].guiActive = this.Events["FreeAttachStraight"].guiActiveEditor = this.Events["FreeAttach"].active;
            }
            if (!this.IsDocked && !this.IsLinked)
            {
                this.Events["ResetId"].active = this.Events["ResetId"].guiActive = this.Events["ResetId"].guiActiveEditor = true;
            }
            else
            {
                this.Events["ResetId"].active = this.Events["ResetId"].guiActive = this.Events["ResetId"].guiActiveEditor = false;
            }
        }

        private Vector3 _convertFreeAttachRayHitPointToStrutTarget()
        {
            var offset = this.FreeAttachPositionOffset;
            if ((this.FreeAttachPositionOffsetVectorSetInEditor && HighLogic.LoadedSceneIsFlight) || HighLogic.LoadedSceneIsEditor)
            {
                offset = this.FreeAttachPart.transform.rotation*offset;
            }
            var targetPos =
                Util.Util.PerformRaycast(this.Origin.position,
                                         this.FreeAttachPart.transform.position +
                                         offset, this.Origin.right).Hit.point;
            return targetPos;
        }

        private void _delayedStart()
        {
            if (this._ticksForDelayedStart > 0)
            {
                this._ticksForDelayedStart--;
                return;
            }
            this._delayedStartFlag = false;
            if (this.Id == Guid.Empty.ToString())
            {
                this.Id = Guid.NewGuid().ToString();
            }
            if (this.IsLinked) // && !HighLogic.LoadedSceneIsEditor)
            {
                if (this.IsTargetOnly)
                {
                    this.Mode = Mode.Linked;
                }
                else
                {
                    this.Reconnect();
                }
            }
            else
            {
                this.Mode = Mode.Unlinked;
            }
            this.Events.Sort((l, r) =>
                             {
                                 if (l.name == "Link" && r.name == "FreeAttach")
                                 {
                                     return -1;
                                 }
                                 if (r.name == "Link" && l.name == "FreeAttach")
                                 {
                                     return 1;
                                 }
                                 if (l.name == "FreeAttach" && r.name == "FreeAttachStraight")
                                 {
                                     return -1;
                                 }
                                 if (r.name == "FreeAttach" && l.name == "FreeAttachStraight")
                                 {
                                     return 1;
                                 }
                                 if (l.name == "Link" && r.name == "FreeAttachStraight")
                                 {
                                     return -1;
                                 }
                                 if (r.name == "Link" && l.name == "FreeAttachStraight")
                                 {
                                     return 1;
                                 }
                                 return string.Compare(l.name, r.name, StringComparison.Ordinal);
                             }
                );
            this.UpdateGui();
        }

        [KSPEvent(name = "Dock", active = false, guiName = "Dock with Target", guiActiveEditor = false, guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void Dock()
        {
            if (HighLogic.LoadedSceneIsEditor || !this.IsLinked || !this.IsConnectionOrigin || this.IsTargetOnly || this.IsOwnVesselConnected || (this.IsFreeAttached ? FreeAttachPart == null : Target == null) || this.IsDocked)
            {
                OSD.Warn("Can't dock.");
                return;
            }
            this.DockingVesselName = this.vessel.GetName();
            this.DockingVesselTypeString = this.vessel.vesselType.ToString();
            this.DockingVesselId = this.vessel.rootPart.flightID;
            this.IsDocked = true;
            if (this.IsFreeAttached)
            {
                this.FreeAttachPart.Couple(this.part);
            }
            else
            {
                this.Target.part.Couple(this.part);
            }
            this.UpdateGui();
        }

        [KSPEvent(name = "UnDock", active = false, guiName = "Undock from Target", guiActiveEditor = false, guiActiveUnfocused = true, unfocusedRange = Config.UnfocusedRange)]
        public void UnDock()
        {
            this.ProcessUnDock();
        }

        private void _manageAttachNode(float breakForce)
        {
            if (!this.IsConnectionOrigin || this.IsTargetOnly || this._jointAttachNode != null || !HighLogic.LoadedSceneIsFlight)
            {
                return;
            }
            try
            {
                //Debug.Log("[AS] trying to create partJoint");
                var targetPart = this.IsFreeAttached ? this.FreeAttachPart : this.Target.part;
                if (targetPart == null)
                {
                    return;
                }
                var normDir = (this.Origin.position - (this.IsFreeAttached ? this._convertFreeAttachRayHitPointToStrutTarget() : this.Target.Origin.position)).normalized;
                //var force = (this.IsFreeAttached ? LinkType.Weak : this.Target.IsTargetOnly ? LinkType.Normal : LinkType.Maximum).GetJointStrength(); // + 1;
                this._jointAttachNode = new AttachNode {id = Guid.NewGuid().ToString(), attachedPart = targetPart};
                this._jointAttachNode.breakingForce = this._jointAttachNode.breakingTorque = Mathf.Infinity;
                this._jointAttachNode.position = targetPart.partTransform.InverseTransformPoint(this.IsFreeAttached ? this._convertFreeAttachRayHitPointToStrutTarget() : targetPart.partTransform.position);
                this._jointAttachNode.orientation = targetPart.partTransform.InverseTransformDirection(normDir);
                this._jointAttachNode.size = 1;
                this._jointAttachNode.ResourceXFeed = false;
                this._jointAttachNode.attachMethod = AttachNodeMethod.FIXED_JOINT;
                this.part.attachNodes.Add(this._jointAttachNode);
                this._jointAttachNode.owner = this.part;
                this._partJoint = PartJoint.Create(this.part, this.IsFreeAttached ? targetPart : (targetPart.parent ?? targetPart), this._jointAttachNode, null, AttachModes.SRF_ATTACH);
                //Debug.Log("[AS] part joint created");
            }
            catch (Exception e)
            {
                this._jointAttachNode = null;
                Debug.Log("[AS] failed to create attachjoint: " + e.Message + " " + e.StackTrace);
            }
        }

        private void _realignStrut()
        {
            if (this.IsFreeAttached)
            {
                lock (this._freeAttachStrutUpdateLock)
                {
                    var targetPos = this._convertFreeAttachRayHitPointToStrutTarget();
                    this.DestroyStrut();
                    this.CreateStrut(targetPos);
                }
            }
            else if (!this.IsTargetOnly)
            {
                if (this.Target == null)
                {
                    return;
                }
                this.DestroyStrut();
                if (this.Target.IsTargetOnly)
                {
                    this.CreateStrut(this.Target.Origin.position);
                }
                else
                {
                    this.Target.DestroyStrut();
                    this.CreateStrut(this.Target.Origin.position, 0.5f);
                    this.Target.CreateStrut(this.Origin.position, 0.5f);
                }
            }
        }
    }
}