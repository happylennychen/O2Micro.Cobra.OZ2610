﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.ComponentModel;
using Cobra.Communication;
using Cobra.Common;
using System.IO;

namespace Cobra.OZ2610
{
    internal class EFUSEConfigDEMBehaviorManage:DEMBehaviorManageBase
    {
        #region 基础服务功能设计
        public override UInt32 Command(ref TASKMessage msg)
        {
            UInt32 ret = LibErrorCode.IDS_ERR_SUCCESSFUL;

            switch ((ElementDefine.COMMAND)msg.sub_task)
            {
                case ElementDefine.COMMAND.EFUSE_CONFIG_READ:
                    {
                        if (msg.task_parameterlist.parameterlist.Count < ElementDefine.EF_TOTAL_PARAMS)
                            return ElementDefine.IDS_ERR_DEM_ONE_PARAM_DISABLE;
                        ret = SetWorkMode(ElementDefine.EFUSE_MODE.PROGRAM);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        msg.gm.message = "Please provide 7.2V power supply to Tref pin and limit its current to 150mA.";
                        msg.controlreq = COMMON_CONTROL.COMMON_CONTROL_SELECT;
                        if (!msg.controlmsg.bcancel) return LibErrorCode.IDS_ERR_DEM_USER_QUIT;
                        msg.percent = 40;
                        ret = GetRegisteInfor(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        msg.percent = 60;
                        ret = Read(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        msg.gm.message = "Please remove 7.2V power supply from Tref pin.";
                        msg.controlreq = COMMON_CONTROL.COMMON_CONTROL_SELECT;
                        if (!msg.controlmsg.bcancel) return LibErrorCode.IDS_ERR_DEM_USER_QUIT;
                        ret = SetWorkMode(ElementDefine.EFUSE_MODE.NORMAL);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        msg.percent = 80;
                        ret = ConvertHexToPhysical(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        break;
                    }
                case ElementDefine.COMMAND.EFUSE_CONFIG_WRITE:
                    {
                        if (msg.task_parameterlist.parameterlist.Count < ElementDefine.EF_TOTAL_PARAMS)
                            return ElementDefine.IDS_ERR_DEM_ONE_PARAM_DISABLE;
                        ret = SetWorkMode(ElementDefine.EFUSE_MODE.PROGRAM);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;

                        msg.gm.message = "Please provide 7.2V power supply to Tref pin and limit its current to 150mA.";
                        msg.controlreq = COMMON_CONTROL.COMMON_CONTROL_SELECT;
                        if (!msg.controlmsg.bcancel) return LibErrorCode.IDS_ERR_DEM_USER_QUIT;

                        if (isEFFrozen())
                        {
                            ret = ElementDefine.IDS_ERR_DEM_FROZEN;
                            return ret;
                        }
                        msg.percent = 30;
                        ret = GetRegisteInfor(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        msg.percent = 40;
                        ret = Read(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        msg.percent = 50;
                        ret = ConvertPhysicalToHex(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        //可以在这里添加检查代码，检查0x10是否正确
                        PrepareHexData();
                        msg.percent = 60;
                        Dictionary<uint, ushort> writedata = StoreParameters(msg.task_parameterlist.parameterlist);
                        ret = Write(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        Mapping();
                        ret = Read(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        Dictionary<uint, ushort> readdata = StoreParameters(msg.task_parameterlist.parameterlist);
                        ret = ReadBackCheck(writedata, readdata);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        msg.gm.message = "Please remove 7.2V power supply from Tref pin.";
                        msg.controlreq = COMMON_CONTROL.COMMON_CONTROL_SELECT;
                        if (!msg.controlmsg.bcancel) return LibErrorCode.IDS_ERR_DEM_USER_QUIT;
                        ret = SetWorkMode(ElementDefine.EFUSE_MODE.NORMAL);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        msg.percent = 80;
                        ret = ConvertHexToPhysical(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        break;
                    }
                case ElementDefine.COMMAND.EFUSE_CONFIG_SAVE_EFUSE_HEX:
                    {
                        InitEfuseData();
                        ret = ConvertPhysicalToHex(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        PrepareHexData();
                        ret = GetEfuseHexData(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;
                        FileStream hexfile = new FileStream(msg.sub_task_json, FileMode.Create);
                        StreamWriter hexsw = new StreamWriter(hexfile);
                        hexsw.Write(msg.sm.efusehexdata);
                        hexsw.Close();
                        hexfile.Close();

                        ret = GetEfuseBinData(ref msg);
                        if (ret != LibErrorCode.IDS_ERR_SUCCESSFUL)
                            return ret;

                        string binfilename = Path.Combine(Path.GetDirectoryName(msg.sub_task_json),
                            Path.GetFileNameWithoutExtension(msg.sub_task_json) + ".bin");

                        Encoding ec = Encoding.UTF8;
                        using (BinaryWriter bw = new BinaryWriter(File.Open(binfilename, FileMode.Create), ec))
                        {
                            foreach (var b in msg.sm.efusebindata)
                                bw.Write(b);

                            bw.Close();
                        }
                        break;
                    }
            }
            return ret;
        }
        public override UInt32 Write(ref TASKMessage msg)   //跳过ATE区域
        {
            UInt32 ret = LibErrorCode.IDS_ERR_SUCCESSFUL;
            List<byte> OpReglist = new List<byte>();

            OpReglist = Utility.GenerateRegisterList(ref msg);
            if (OpReglist == null)
                return ret;
            foreach (byte badd in OpReglist)
            {
                if (badd == 0x10)       //跳过ATE区域
                    continue;
                ret = WriteByte(badd, (byte)parent.m_OpRegImg[badd].val);
                parent.m_OpRegImg[badd].err = ret;
            }

            return ret;
        }

        private uint ReadBackCheck(Dictionary<uint, ushort> writedata, Dictionary<uint, ushort> readdata)
        {
            UInt32 ret = LibErrorCode.IDS_ERR_SUCCESSFUL;
            int num = 0;
            //ushort rdata = 0, wdata = 0;
            StringBuilder sb = new StringBuilder();
            StringBuilder sb1 = new StringBuilder();
            foreach (var guid in writedata.Keys)
            {
                if (writedata[guid] != readdata[guid])
                {
                    if (num < 3)
                        sb.Append(string.Format("Write {0} is 0x{1:x4},Read back is 0x{2:x4}.\n", GetParamNameByGUID(guid), writedata[guid], readdata[guid]));
                    sb1.Append(string.Format("Write {0} is 0x{1:x4},Read back is 0x{2:x4}.\n", GetParamNameByGUID(guid), writedata[guid], readdata[guid]));
                    num++;
                }
            }
            if (!string.IsNullOrEmpty(sb.ToString()))
            {
                if (num > 3) sb.Insert(0, string.Format("Total Error:{0},Please check the log file.\n", num));
                parent.m_dynamicErrorLib_dic[ElementDefine.IDS_ERR_DEM_READ_BACK_CHECK_FAILED] = sb.ToString();
                ret = ElementDefine.IDS_ERR_DEM_READ_BACK_CHECK_FAILED;
                FolderMap.WriteFile(sb1.ToString());
            }
            return ret;
        }

        private string GetParamNameByGUID(uint guid)
        {
            string output;
            switch (guid)
            {
                //case 0x00031007: output = "DOT_TH(0x10)"; break;
                //case 0x00031800: output = "Tot-ut-dly"; break;
                //case 0x00031802: output = "DOT_TH(0x18)"; break;
                //case 0x00031804: output = "COT_TH"; break;
                case 0x00031805: output = "Ncell"; break;
                case 0x00031900: output = "Vovp"; break;
                case 0x00031907: output = "UV_ON_CHGPAD"; break;
                case 0x00031A00: output = "Vuvp"; break;
                case 0x00031A04: output = "Vuvr-hys"; break;
                case 0x00031A07: output = "BAT_TYPE"; break;
                case 0x00031B00: output = "DSGPAD_TYPE"; break;
                case 0x00031B01: output = "DSGPAD_ACTIVE_O"; break;
                case 0x00031B02: output = "CHGPAD_TYPE"; break;
                case 0x00031B03: output = "CHGPAD_ACTIVE_O"; break;
                //case 0x00031B05: output = "DOT_ON_SDA_DIS"; break;
                //case 0x00031B06: output = "THM_SHARE"; break;
                case 0x00031C00: output = "UVP_DIS"; break;
                //case 0x00031C01: output = "DOT_DIS"; break;
                //case 0x00031C02: output = "COT_DIS"; break;
                //case 0x00031C03: output = "CUT_DIS"; break;
                case 0x00031C04: output = "CTO_DIS"; break;
                //case 0x00031C05: output = "ALERT_OPTION"; break;
                case 0x00031C07: output = "Tsc"; break;
                case 0x00031D00: output = "Tovp"; break;
                case 0x00031D02: output = "Tuvp"; break;
                case 0x00031D04: output = "Vovr-hys"; break;
                default: output = "unknown"; break;
            }
            return output;
        }

        private void Mapping()
        {
            byte tmp = 0;
            ReadByte((byte)ElementDefine.WORKMODE_REG, ref tmp);
            WriteByte((byte)ElementDefine.WORKMODE_REG, (byte)(tmp & 0xdf));

            ReadByte((byte)ElementDefine.OP_SW_MAPPING, ref tmp);
            WriteByte((byte)ElementDefine.OP_SW_MAPPING, (byte)(tmp | 0x08));
        }

        private Dictionary<uint, ushort> StoreParameters(AsyncObservableCollection<Parameter> parameterlist)
        {
            //Dictionary<byte, ushort> output = new Dictionary<byte, ushort>();
            //for (byte i = (byte)ElementDefine.EF_USR_OFFSET; i <= ElementDefine.EF_USR_TOP; i++)
            //{
            //    output.Add(i, parent.m_OpRegImg[i].val);
            //}
            //return output;
            ushort rdata = 0;
            Dictionary<uint, ushort> output = new Dictionary<uint, ushort>();
            foreach (var param in parameterlist)
            {
                dem_dm.ReadFromRegImg(param, ref rdata);
                output.Add(param.guid, rdata);
            }
            return output;
        }

        private bool isEFFrozen()
        {
            byte tmp = 0;
            ReadByte((byte)ElementDefine.EF_USR_TOP, ref tmp);
            if ((tmp & 0x80) == 0x80)
                return true;
            else
                return false;
        }

        private void InitEfuseData()
        {
            for (ushort i = ElementDefine.EF_USR_OFFSET; i <= ElementDefine.EF_USR_TOP; i++)
            {
                parent.m_OpRegImg[i].err = 0;
                parent.m_OpRegImg[i].val = 0;
            }
        }

        private UInt32 GetEfuseHexData(ref TASKMessage msg)
        {
            string tmp = "";
            for (ushort i = ElementDefine.EF_USR_OFFSET; i <= ElementDefine.EF_USR_TOP; i++)
            {
                if (parent.m_OpRegImg[i].err != LibErrorCode.IDS_ERR_SUCCESSFUL)
                    return parent.m_OpRegImg[i].err;
                tmp += "0x" + i.ToString("X2") + ", " + "0x" + parent.m_OpRegImg[i].val.ToString("X2") + "\r\n";
            }
            msg.sm.efusehexdata = tmp;
            return LibErrorCode.IDS_ERR_SUCCESSFUL;
        }

        private UInt32 GetEfuseBinData(ref TASKMessage msg)
        {
            List<byte> tmp = new List<byte>();
            for (ushort i = ElementDefine.EF_USR_OFFSET; i <= ElementDefine.EF_USR_TOP; i++)
            {
                if (parent.m_OpRegImg[i].err != LibErrorCode.IDS_ERR_SUCCESSFUL)
                    return parent.m_OpRegImg[i].err;
                tmp.Add((byte)i);
                tmp.Add((byte)(parent.m_OpRegImg[i].val));
            }
            msg.sm.efusebindata = tmp;
            return LibErrorCode.IDS_ERR_SUCCESSFUL;
        }
        #endregion
    }
}