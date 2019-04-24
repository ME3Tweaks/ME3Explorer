﻿//This class was generated by ME3Explorer
//Author: Warranty Voider
//URL: http://sourceforge.net/projects/me3explorer/
//URL: http://me3explorer.freeforums.org/
//URL: http://www.facebook.com/pages/Creating-new-end-for-Mass-Effect-3/145902408865659
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ME3Explorer.Unreal;
using ME3Explorer.Packages;

namespace ME3Explorer.Unreal.Classes
{
    public class AnimTree
    {
        #region Unreal Props

        //Float Properties

        public float NodeTotalWeight;

        //Array Properties

        public List<AnimGroupEntry> AnimGroups;
        public List<string> ComposePrePassBoneNames;
        public List<SkelControlListEntry> SkelControlLists;
        public List<ChildrenEntry> Children;

        public struct ChildrenEntry
        {
            public string Name;
            public float Weight;
            public int Anim;
            public bool bMirrorSkeleton;
            public bool bIsAdditive;
        }
        public struct SkelControlListEntry
        {
            public string BoneName;
            public int ControlHead;
        }
        public struct AnimGroupEntry
        {
            public string GroupName;
            public float RateScale;
            public float SynchPctPosition;
        }

        #endregion
        
        public int MyIndex;
        public ME3Package pcc;
        public byte[] data;
        public List<PropertyReader.Property> Props;

        public AnimTree(ME3Package Pcc, int Index)
        {
            pcc = Pcc;
            MyIndex = Index;
            if (pcc.isExport(Index))
                data = pcc.Exports[Index].Data;
            Props = PropertyReader.getPropList(pcc.Exports[Index]);
            
            AnimGroups = new List<AnimGroupEntry>();
            ComposePrePassBoneNames = new List<string>();
            SkelControlLists = new List<SkelControlListEntry>();
            Children = new List<ChildrenEntry>();
            
            foreach (PropertyReader.Property p in Props)
                switch (pcc.getNameEntry(p.Name))
                {

                    case "NodeTotalWeight":
                        NodeTotalWeight = BitConverter.ToSingle(p.raw, p.raw.Length - 4);
                        break;
                    case "AnimGroups":
                        ReadAnimGroups(p.raw);
                        break;
                    case "ComposePrePassBoneNames":
                        ReadPrePassBoneNames(p.raw);
                        break;
                    case "SkelControlLists":
                        ReadSkelControlLists(p.raw);
                        break;
                    case "Children":
                        ReadChildren(p.raw);
                        break;
                }
        }

        public void ReadAnimGroups(byte[] raw)
        {
            int count = GetArrayCount(raw);
            byte[] buff = GetArrayContent(raw);
            int pos = 0;
            for (int i = 0; i < count; i++)
            {
                List<PropertyReader.Property> pp = PropertyReader.ReadProp(pcc, buff, pos);
                pos = pp[pp.Count - 1].offend;
                AnimGroupEntry e = new AnimGroupEntry();
                foreach(PropertyReader.Property p in pp)
                    switch (pcc.getNameEntry(p.Name))
                    {
                        case "GroupName":
                            e.GroupName = pcc.getNameEntry(p.Value.IntValue);
                            break;
                        case "RateScale":
                            e.RateScale = BitConverter.ToSingle(p.raw, p.raw.Length - 4);
                            break;
                        case "SynchPctPosition":
                            e.SynchPctPosition = BitConverter.ToSingle(p.raw, p.raw.Length - 4);
                            break;                        
                    }
                AnimGroups.Add(e);
            }
        }

        public void ReadPrePassBoneNames(byte[] raw)
        {
            int count = GetArrayCount(raw);
            byte[] buff = GetArrayContent(raw);
            for (int i = 0; i < count; i++)
                ComposePrePassBoneNames.Add(pcc.getNameEntry(BitConverter.ToInt32(buff, i * 8)));
        }

        public void ReadSkelControlLists(byte[] raw)
        {
            int count = GetArrayCount(raw);
            byte[] buff = GetArrayContent(raw);
            int pos = 0;
            for (int i = 0; i < count; i++)
            {
                List<PropertyReader.Property> pp = PropertyReader.ReadProp(pcc, buff, pos);
                pos = pp[pp.Count - 1].offend;
                SkelControlListEntry e = new SkelControlListEntry();
                foreach (PropertyReader.Property p in pp)
                    switch (pcc.getNameEntry(p.Name))
                    {
                        case "BoneName":
                            e.BoneName = pcc.getNameEntry(p.Value.IntValue);
                            break;
                        case "ControlHead":
                            e.ControlHead = p.Value.IntValue;
                            break;
                    }
                SkelControlLists.Add(e);
            }
        }

        public void ReadChildren(byte[] raw)
        {
            int count = GetArrayCount(raw);
            byte[] buff = GetArrayContent(raw);
            int pos = 0;
            for (int i = 0; i < count; i++)
            {
                List<PropertyReader.Property> pp = PropertyReader.ReadProp(pcc, buff, pos);
                pos = pp[pp.Count - 1].offend;
                ChildrenEntry e = new ChildrenEntry();
                foreach (PropertyReader.Property p in pp)
                    switch (pcc.getNameEntry(p.Name))
                    {
                        case "Name":
                            e.Name = pcc.getNameEntry(p.Value.IntValue);
                            break;
                        case "Weight":
                            e.Weight = BitConverter.ToSingle(p.raw, p.raw.Length - 4);
                            break;
                        case "Anim":
                            e.Anim = p.Value.IntValue;
                            break;
                        case "bMirrorSkeleton":
                            e.bMirrorSkeleton = (p.raw[p.raw.Length - 1] == 1);
                            break;
                        case "bIsAdditive":
                            e.bIsAdditive = (p.raw[p.raw.Length - 1] == 1);
                            break;
                    }
                Children.Add(e);
            }
        }

        public int GetArrayCount(byte[] raw)
        {
            return BitConverter.ToInt32(raw, 24);
        }

        public byte[] GetArrayContent(byte[] raw)
        {
            byte[] buff = new byte[raw.Length - 28];
            for (int i = 0; i < raw.Length - 28; i++)
                buff[i] = raw[i + 28];
            return buff;
        }

        public TreeNode ToTree()
        {
            TreeNode res = new TreeNode("AnimTree : " + pcc.Exports[MyIndex].ObjectName + "(#" + MyIndex + ")");
            res.Nodes.Add("NodeTotalWeight : " + NodeTotalWeight);
            res.Nodes.Add(AnimGroupsToTree());
            res.Nodes.Add(PrePassBoneNamesToTree());
            res.Nodes.Add(SkelControlListsToTree());
            res.Nodes.Add(ChildrenToTree());
            return res;
        }

        public TreeNode AnimGroupsToTree()
        {
            TreeNode res = new TreeNode("Animation Groups");
            for (int i = 0; i < AnimGroups.Count; i++)
            {
                TreeNode t = new TreeNode(i.ToString());
                t.Nodes.Add("Group Name : " + AnimGroups[i].GroupName);
                t.Nodes.Add("Rate Scale : " + AnimGroups[i].RateScale);
                t.Nodes.Add("SynchPctPosition : " + AnimGroups[i].SynchPctPosition);
                res.Nodes.Add(t);
            }
            return res;
        }

        public TreeNode PrePassBoneNamesToTree()
        {
            TreeNode res = new TreeNode("Compose Pre Pass Bone Names");
            for (int i = 0; i < ComposePrePassBoneNames.Count; i++)
                res.Nodes.Add(i + " : " + ComposePrePassBoneNames[i]);
            return res;
        }

        public TreeNode SkelControlListsToTree()
        {
            TreeNode res = new TreeNode("Skel Control Lists");
            for (int i = 0; i < SkelControlLists.Count; i++)
            {
                TreeNode t = new TreeNode(i.ToString());
                t.Nodes.Add("Bone Name : " + SkelControlLists[i].BoneName);
                t.Nodes.Add("Control Head : " + SkelControlLists[i].ControlHead);
                res.Nodes.Add(t);
            }
            return res;
        }

        public TreeNode ChildrenToTree()
        {
            TreeNode res = new TreeNode("Children");
            for (int i = 0; i < Children.Count; i++)
            {
                int idx = Children[i].Anim;
                TreeNode t = new TreeNode(i.ToString());
                t.Nodes.Add("Name : " + Children[i].Name);
                t.Nodes.Add("Weight : " + Children[i].Weight);
                t.Nodes.Add("Anim : " + Children[i].Anim);
                if (pcc.isExport(idx))
                    switch (pcc.Exports[idx].ClassName)
                    {
                        case "AnimNodeSlot":
                            AnimNodeSlot ans = new AnimNodeSlot(pcc, idx);
                            t.Nodes.Add(ans.ToTree());
                            break;
                    }
                t.Nodes.Add("bIsMirrorSkeleton : " + Children[i].bMirrorSkeleton);
                t.Nodes.Add("bIsAdditive : " + Children[i].bIsAdditive);
                res.Nodes.Add(t);
            }
            return res;
        }

    }
}