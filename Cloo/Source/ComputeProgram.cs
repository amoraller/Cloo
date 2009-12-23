﻿/*

Copyright (c) 2009 Fatjon Sakiqi

Permission is hereby granted, free of charge, to any person
obtaining a copy of this software and associated documentation
files (the "Software"), to deal in the Software without
restriction, including without limitation the rights to use,
copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following
conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
OTHER DEALINGS IN THE SOFTWARE.

*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using OpenTK.Compute.CL10;


namespace Cloo
{
    public class ComputeProgram: ComputeResource
    {
        #region Fields

        private readonly ComputeContext context;
        private readonly ReadOnlyCollection<ComputeDevice> devices;
        private readonly string source;
        private ReadOnlyCollection<byte[]> binaries;
        private string buildOptions;
        
        #endregion

        #region Properties

        /// <summary>
        /// Return the program binaries for all devices associated with program. he bits returned can be an implementation-specific intermediate representation (a.k.a. IR) or device specific executable bits or both. The decision on which information is returned in the binary is up to the OpenCL implementation.
        /// </summary>
        public ReadOnlyCollection<byte[]> Binaries
        {
            get
            {
                return binaries;
            }
        }

        /// <summary>
        /// Return the build options specified by the options argument when the program is created.
        /// </summary>
        public string BuildOptions
        {
            get
            {
                return buildOptions;
            }
        }

        /// <summary>
        /// Return the context specified when the program object is created
        /// </summary>
        public ComputeContext Context
        {
            get
            {
                return context;
            }
        }

        /// <summary>
        /// Return the list of devices associated with the program object. This can be the devices associated with context on which the program object has been created or can be a subset of devices that are specified when a progam object is created from binaries.
        /// </summary>
        public ReadOnlyCollection<ComputeDevice> Devices
        {
            get
            {
                return devices;
            }
        }

        /// <summary>
        /// Return the program source code specified when creating the program. null if program was created from binaries.
        /// </summary>
        public string Source
        {
            get
            {
                return source;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a program for a context, using the source code specified. The devices associated with the program are the devices associated with context.
        /// </summary>
        /// <param name="context">A valid OpenCL context.</param>
        /// <param name="source">The source code for this program.</param>
        public ComputeProgram( ComputeContext context, string source )
        {
            ErrorCode error = ErrorCode.Success;
            unsafe 
            { 
                Handle = CL.CreateProgramWithSource( 
                    context.Handle, 
                    1, 
                    new string[]{ source }, 
                    ( IntPtr* )null, 
                    &error );
            }
            ComputeException.ThrowIfError( error );

            this.context = context;
            this.devices = context.Devices;
            this.source = source;
        }

        /// <summary>
        /// Creates a program for a context, using the binaries specified.
        /// </summary>
        /// <param name="context">A valid OpenCL context.</param>
        /// <param name="binaries">The binaries to be assigned to the devices.</param>
        /// <param name="devices">A subset of the devices associated with the context or null for all the devices associated with the context.</param>
        public ComputeProgram( ComputeContext context, IList<byte[]> binaries, IList<ComputeDevice> devices )
        {
            int count = binaries.Count;            
            
            IntPtr[] deviceHandles = ( devices != null ) ?
                ExtractHandles<ComputeDevice>( devices ) :
                ExtractHandles<ComputeDevice>( context.Devices );
                        
            IntPtr[] binariesPtrs = new IntPtr[ count ];
            IntPtr[] binariesLengths = new IntPtr[ count ];
            int[] binariesStats = new int[ count ];
            ErrorCode error = ErrorCode.Success;
            GCHandle binariesPtrGCHandle = new GCHandle();
            GCHandle[] binariesGCHandles = new GCHandle[ count ];

            try
            {
                binariesPtrGCHandle = GCHandle.Alloc( binariesPtrs, GCHandleType.Pinned );
                for( int i = 0; i < count; i++ )
                {
                    binariesGCHandles[ i ] = GCHandle.Alloc( binaries[ i ], GCHandleType.Pinned );
                    binariesPtrs[ i ] = binariesGCHandles[ i ].AddrOfPinnedObject();
                    binariesLengths[ i ] = new IntPtr( binaries[ i ].Length );
                }

                unsafe
                {                    
                    byte** binariesPtr = ( byte** )binariesPtrGCHandle.AddrOfPinnedObject();
                    fixed( IntPtr* binariesLengthsPtr = binariesLengths )
                    fixed( IntPtr* deviceHandlesPtr = deviceHandles )
                    fixed( int* binaryStatusPtr = binariesStats )
                        Handle = CL.CreateProgramWithBinary(
                            context.Handle,
                            count,
                            deviceHandlesPtr,
                            binariesLengthsPtr,
                            binariesPtr,
                            binaryStatusPtr,
                            &error );
                }
            }
            finally
            {
                binariesPtrGCHandle.Free();
                for( int i = 0; i < count; i++ )
                    binariesGCHandles[ i ].Free();
            }

            ComputeException.ThrowIfError( error );

            this.binaries = new ReadOnlyCollection<byte[]>( binaries );
            this.context = context;
            this.devices = new ReadOnlyCollection<ComputeDevice>( 
                ( devices != null ) ? devices : context.Devices );
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Builds (compiles and links) a program executable from the program source or binary for all the devices or some specific devices in the OpenCL context associated with program.
        /// </summary>
        /// <param name="devices">A list of devices associated with program. If the list is null, the program executable is built for all devices associated with program for which a source or a binary has been loaded.</param>
        /// <param name="options">The set of options for the OpenCL compiler.</param>
        public void Build( ICollection<ComputeDevice> devices, string options, NotifyDelegate notifyDelegate, IntPtr notifyData )
        {
            IntPtr notifyDelegatePtr = ( notifyDelegate != null ) ? Marshal.GetFunctionPointerForDelegate( notifyDelegate ) : IntPtr.Zero;
            buildOptions = ( options != null ) ? options : "";

            int error;            
            if( devices != null )
            {
                IntPtr[] deviceHandles = ComputeObject.ExtractHandles( devices );
                error = CL.BuildProgram( Handle, deviceHandles.Length, deviceHandles, options, notifyDelegatePtr, notifyData );
            }
            else
            {
                error = CL.BuildProgram( Handle, 0, ( IntPtr[] )null, options, notifyDelegatePtr, notifyData );
            }

            if( error == ( int )ErrorCode.Success )
            {
                binaries = GetBinaries();
            }

            ComputeException.ThrowIfError( error );         
        }

        /// <summary>
        /// Creates kernel objects for all kernel functions in program. Kernel objects are not created for any __kernel functions in program that do not have the same function definition across all devices for which a program executable has been successfully built.
        /// </summary>
        public ICollection<ComputeKernel> CreateAllKernels()
        {
            ICollection<ComputeKernel> kernels = new Collection<ComputeKernel>();
            int kernelsCount = 0;
            IntPtr[] kernelHandles;

            unsafe
            {
                int error = CL.CreateKernelsInProgram( Handle, 0, ( IntPtr* )null, &kernelsCount );
                ComputeException.ThrowIfError( error );
                
                kernelHandles = new IntPtr[ kernelsCount ];
                error = CL.CreateKernelsInProgram( Handle, kernelsCount, kernelHandles, null );
                ComputeException.ThrowIfError( error );
            }

            for( int i = 0; i < kernelsCount; i++ )
                kernels.Add( new ComputeKernel( kernelHandles[ i ], this ) );

            return kernels;
        }

        /// <summary>
        /// Creates a kernel object for the kernel function specified by the function name.
        /// </summary>
        public ComputeKernel CreateKernel( string functionName )
        {
            return new ComputeKernel( functionName, this );
        }

        /// <summary>
        /// Gets the build log of program for the specified device.
        /// </summary>
        public string GetBuildLog( ComputeDevice device )
        {
            return GetStringInfo<ProgramBuildInfo>(
                device,
                ProgramBuildInfo.ProgramBuildLog,
                CL.GetProgramBuildInfo );
        }

        /// <summary>
        /// Gets the build status of program for the specified device.
        /// </summary>
        public BuildStatus GetBuildStatus( ComputeDevice device )
        {
            return ( BuildStatus )GetInfo<ProgramBuildInfo, uint>(
                device,
                ProgramBuildInfo.ProgramBuildStatus,
                CL.GetProgramBuildInfo );
        }

        /// <summary>
        /// Gets a string representation of this program.
        /// </summary>
        public override string ToString()
        {
            return "ComputeProgram" + base.ToString();
        }

        #endregion

        #region Protected methods

        protected override void Dispose( bool manual )
        {
            if( manual )
            {
                //dispose managed resources
            }

            if( Handle != IntPtr.Zero )
            {
                CL.ReleaseProgram( Handle );
                Handle = IntPtr.Zero;
            }
        }

        #endregion

        #region Private methods

        private ReadOnlyCollection<byte[]> GetBinaries()
        {
            IntPtr[] binaryLengths = GetArrayInfo<ProgramInfo, IntPtr>(
                ProgramInfo.ProgramBinarySizes, CL.GetProgramInfo );

            GCHandle[] binariesGCHandles = new GCHandle[ binaryLengths.Length ];
            IntPtr[] binariesPtrs = new IntPtr[ binaryLengths.Length ];
            IList<byte[]> binaries = new List<byte[]>();

            int error = ( int )ErrorCode.Success;
            try
            {
                for( int i = 0; i < binaryLengths.Length; i++ )
                {
                    byte[] binary = new byte[ binaryLengths[ i ].ToInt64() ];
                    binariesGCHandles[ i ] = GCHandle.Alloc( binary, GCHandleType.Pinned );
                    binariesPtrs[ i ] = binariesGCHandles[ i ].AddrOfPinnedObject();
                    binaries.Add( binary );
                }

                unsafe
                {
                    error = CL.GetProgramInfo(
                        Handle,
                        ProgramInfo.ProgramBinaries,
                        new IntPtr( binariesPtrs.Length * sizeof( IntPtr ) ),
                        binariesPtrs,
                        ( IntPtr* )null );
                }
            }
            finally
            {
                for( int i = 0; i < binaryLengths.Length; i++ )
                    binariesGCHandles[ i ].Free();
            }

            ComputeException.ThrowIfError( error );

            return new ReadOnlyCollection<byte[]>( binaries );
        }

        #endregion

        #region Delegates

        public delegate void NotifyDelegate( IntPtr program, IntPtr dataPtr );

        #endregion
    }
}