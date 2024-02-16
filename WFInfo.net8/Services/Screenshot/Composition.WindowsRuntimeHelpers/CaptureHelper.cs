﻿//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  The MIT License (MIT)
//
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
//
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
//
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace WFInfo.Services.Screenshot.Composition.WindowsRuntimeHelpers;

public static class CaptureHelper
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid);

        IntPtr CreateForMonitor(
            [In] IntPtr monitor,
            [In] ref Guid iid);
    }

    public static GraphicsCaptureItem CreateItemForWindow(this nint hWnd)
    {
        var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemPointer = factory.CreateForWindow(hWnd, GraphicsCaptureItemGuid);
        var item = GraphicsCaptureItem.FromAbi(itemPointer);
        Marshal.Release(itemPointer);

        return item;
    }

    public static GraphicsCaptureItem CreateItemForMonitor(this nint hMon)
    {
        var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemPointer = factory.CreateForMonitor(hMon, GraphicsCaptureItemGuid);
        var item = GraphicsCaptureItem.FromAbi(itemPointer);
        Marshal.Release(itemPointer);

        return item;
    }
}
