﻿using System;
using System.Runtime.InteropServices;

namespace CMissionLib.UnitSyncLib
{
	[Serializable]
	[StructLayout(LayoutKind.Sequential)]
	public struct StartPos
	{
		public int x;
		public int z;
	}
}