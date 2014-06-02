﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DockingStrut
{
    public static class DSUtil
    {
        public static List<ModuleDockingStrutBase> GetAllDockingStrutModules(Vessel vessel)
        {
            var partList = (from part in vessel.parts
                            where part.Modules.Contains(ModuleDockingStrutBase.TargetModuleName) || part.Modules.Contains(ModuleDockingStrutBase.TargeterModuleName)
                            select part);
            var moduleList = new List<ModuleDockingStrutBase>();
            foreach (var part in partList)
            {
                if (part.Modules.Contains(ModuleDockingStrutBase.TargetModuleName))
                {
                    moduleList.Add(part.Modules[ModuleDockingStrutBase.TargetModuleName] as ModuleDockingStrutBase);
                }
                if (part.Modules.Contains(ModuleDockingStrutBase.TargeterModuleName))
                {
                    moduleList.Add(part.Modules[ModuleDockingStrutBase.TargeterModuleName] as ModuleDockingStrutBase);
                }
            }
            return moduleList;
        }

        public static Tuple<bool, ModuleDockingStrutBase, ModuleDockingStrutBase> GetDockingStrut(this Vessel v, Guid targetId)
        {
            foreach (var p in from p in v.Parts
                              let targeterFlag = p.Modules.Contains(ModuleDockingStrutBase.TargeterModuleName)
                              let targetFlag = p.Modules.Contains(ModuleDockingStrutBase.TargetModuleName)
                              where targeterFlag || targetFlag
                              where
                                  (targeterFlag && ((p.Modules[ModuleDockingStrutBase.TargeterModuleName] as ModuleDockingStrutBase) != null && (p.Modules[ModuleDockingStrutBase.TargeterModuleName] as ModuleDockingStrutBase).ID == targetId)) ||
                                  (targetFlag && ((p.Modules[ModuleDockingStrutBase.TargetModuleName] as ModuleDockingStrutBase) != null && (p.Modules[ModuleDockingStrutBase.TargetModuleName] as ModuleDockingStrutBase).ID == targetId))
                              select p)
            {
                ModuleDockingStrutBase target = null, targeter = null;
                if (p.Modules.Contains(ModuleDockingStrutBase.TargetModuleName))
                {
                    target = p.Modules[ModuleDockingStrutBase.TargetModuleName] as ModuleDockingStrutBase;
                }
                if (p.Modules.Contains(ModuleDockingStrutBase.TargeterModuleName))
                {
                    targeter = p.Modules[ModuleDockingStrutBase.TargeterModuleName] as ModuleDockingStrutBase;
                }
                return Tuple.New(true, target, targeter);
            }
            return Tuple.New<bool, ModuleDockingStrutBase, ModuleDockingStrutBase>(false, null, null);
        }

        public static Part PartFromHit(RaycastHit hit)
        {
            var go = hit.collider.gameObject;
            var p = Part.FromGO(go);
            while (p == null)
            {
                if (go.transform.parent != null && go.transform.parent.gameObject != null)
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

        public class Tuple<T1, T2>
        {
            public T1 Item1 { get; private set; }
            public T2 Item2 { get; private set; }

            internal Tuple(T1 first, T2 second)
            {
                this.Item1 = first;
                this.Item2 = second;
            }
        }

        public class Tuple<T1, T2, T3> : Tuple<T1, T2>
        {
            public T3 Item3 { get; private set; }

            internal Tuple(T1 first, T2 second, T3 third) : base(first, second)
            {
                this.Item3 = third;
            }
        }

        public static class Tuple
        {
            public static Tuple<T1, T2> New<T1, T2>(T1 first, T2 second)
            {
                var tuple = new Tuple<T1, T2>(first, second);
                return tuple;
            }

            public static Tuple<T1, T2, T3> New<T1, T2, T3>(T1 first, T2 second, T3 third)
            {
                var tuple = new Tuple<T1, T2, T3>(first, second, third);
                return tuple;
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class OSDInfo : MonoBehaviour
    {
        // ReSharper disable once InconsistentNaming
        public void OnGUI()
        {
            OSD.Update();
        }
    }

    /*  Copyright (C) 2013 FW Industries
        Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
        The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
    */

    public static class OSD
    {
        private const string Prefix = "[DockingStrut] ";

        private static readonly List<Message> Msgs = new List<Message>();

        public static void AddMessage(String text, Color color, float shownFor = 3)
        {
            var msg = new Message {Text = Prefix + text, Color = color, HideAt = Time.time + shownFor};
            Msgs.Add(msg);
        }

        private static float CalcHeight()
        {
            var style = CreateStyle(Color.white);
            return Msgs.Aggregate(.0f, (a, m) => a + style.CalcSize(new GUIContent(m.Text)).y);
        }

        private static GUIStyle CreateStyle(Color color)
        {
            var style = new GUIStyle {stretchWidth = true, alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold, normal = {textColor = color}};
            return style;
        }

        public static void Error(String text)
        {
            AddMessage(text, XKCDColors.LightRed);
        }

        public static void Info(String text)
        {
            AddMessage(text, XKCDColors.OffWhite);
        }

        public static void Success(String text)
        {
            AddMessage(text, XKCDColors.Cerulean);
        }

        public static void Update()
        {
            if (Msgs.Count == 0)
            {
                return;
            }
            Msgs.RemoveAll(m => Time.time >= m.HideAt);
            var h = CalcHeight();
            GUILayout.BeginArea(new Rect(0, Screen.height*0.1f, Screen.width, h), CreateStyle(Color.white));
            Msgs.ForEach(m => GUILayout.Label(m.Text, CreateStyle(m.Color)));
            GUILayout.EndArea();
        }

        public static void Warn(String text)
        {
            AddMessage(text, XKCDColors.Yellow);
        }

        private class Message
        {
            public Color Color { get; set; }
            public float HideAt { get; set; }
            public String Text { get; set; }
        }
    }
}