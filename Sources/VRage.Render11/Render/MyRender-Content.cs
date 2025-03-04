﻿using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Collections.Generic;
using System.Diagnostics;
using VRage.Utils;
using VRage.Voxels;
using VRageRender.Resources;

namespace VRageRender
{
    partial class MyRender11
    {
        internal static unsafe void InitSubsystems()
        {
            InitializeBlendStates();
            InitializeRasterizerStates();
            InitilizeSamplerStates();

            MyScene.Init();
            MyRender11.Init();
            MyCommon.Init();
            MyPipelineStates.Init();
            MyTextures.Init();
            MyVertexLayouts.Init();
            MyShaders.Init();
            MyRwTextures.Init();
            MyHwBuffers.Init();
            MyMeshes.Init(); 
            MyMeshTableSRV.Init();
            MyMergeInstancing.Init(); 
            MyLightRendering.Init();
            MyLinesRenderer.Init();
            MySpritesRenderer.Init();
            MyPrimitivesRenderer.Init();
            MyOutline.Init();
            MyBlur.Init();

            MyFoliageComponents.Init();

            MyBillboardRenderer.Init(); // hardcoded limits
            MyDebugRenderer.Init();

            MyScreenDecals.Init();
            MyEnvProbeProcessing.Init();
            MyAtmosphereRenderer.Init();
			MyCloudRenderer.Init();
            MyAAEdgeMarking.Init(); 
            MyScreenPass.Init();
            MyCopyToRT.Init();
            MyBlendTargets.Init();
            MyFXAA.Init();
            MyDepthResolve.Init();
            MyBloom.Init();
            MyLuminanceAverage.Init();
            MyToneMapping.Init();
            MySSAO.Init();
            MyPlanetBlur.Init();
            MyHdrDebugTools.Init();

            MySceneMaterials.Init();
            MyMaterials1.Init();
            MyVoxelMaterials1.Init();
            MyMeshMaterials1.Init();
        }

        internal static void OnDeviceReset()
        {
            MyHwBuffers.OnDeviceReset();
            MyShaders.OnDeviceReset();
            MyMaterialShaders.OnDeviceReset();
            MyPipelineStates.OnDeviceReset();
            MyTextures.OnDeviceReset();
            MyRwTextures.OnDeviceReset();

            ResetShadows(Settings.ShadowCascadeCount, RenderSettings.ShadowQuality.ShadowCascadeResolution());

            MyBillboardRenderer.OnDeviceRestart();
            MyScreenDecals.OnDeviceReset();

            MyMeshMaterials1.InvalidateMaterials();
            MyVoxelMaterials1.InvalidateMaterials();

            MyRenderableComponent.MarkAllDirty();
            foreach (var f in MyComponentFactory<MyFoliageComponent>.GetAll())
            {
                f.Dispose();
            }

            foreach (var c in MyComponentFactory<MyGroupRootComponent>.GetAll())
            {
                c.OnDeviceReset();
            }

            MyBigMeshTable.Table.OnDeviceReset();
            MySceneMaterials.OnDeviceReset();
            MyMeshes.OnDeviceReset();
            MyInstancing.OnDeviceReset();
            MyScreenDecals.OnDeviceReset();
        }

        internal static void OnDeviceEnd()
        {
            MyScreenDecals.OnDeviceEnd();
            MyShaders.OnDeviceEnd();
            MyMaterialShaders.OnDeviceEnd();
            MyVoxelMaterials1.OnDeviceEnd();
            MyTextures.OnDeviceEnd();
            MyRwTextures.OnDeviceEnd();
            MyHwBuffers.OnDeviceEnd();
            MyPipelineStates.OnDeviceEnd();
        }

        internal static void OnSessionEnd()
        {
            UnloadData();
        }

        #region Content load

        internal static void UnloadData()
        {
            MyActorFactory.RemoveAll();
            // many-to-one relation, can live withput owners, deallocated separately
            // MyComponentFactory<MyInstancingComponent>.RemoveAll();

            //MyVoxelMesh.RemoveAll();
            //MyDynamicMesh.RemoveAll();


            MyRender11.Log.WriteLine("Unloading session data");

            MyScene.DynamicRenderablesDBVH.Clear();
            if (MyScene.SeparateGeometry)
                MyScene.StaticRenderablesDBVH.Clear();
            MyScene.GroupsDBVH.Clear();
            MyScene.FoliageDBVH.Clear();
            MyClipmapFactory.RemoveAll();
            MyClipmap.UnloadCache();

            MyInstancing.OnSessionEnd();
            MyFoliageComponents.OnSessionEnd();
            MyMeshes.OnSessionEnd();
            MyLights.OnSessionEnd();

            MyMaterials1.OnSessionEnd();
            MyVoxelMaterials1.OnSessionEnd();
            MyMeshMaterials1.OnSessionEnd();
            MyScreenDecals.OnSessionEnd();
            
            MyTextures.OnSessionEnd();
            MyBigMeshTable.Table.OnSessionEnd();

            MyPrimitivesRenderer.Unload();

            //MyAssetsLoader.ClearMeshes();
        }

        internal static void QueryTexturesFromEntities()
        {
            MyMeshMaterials1.OnResourcesRequesting();
            MyVoxelMaterials1.OnResourcesRequesting();
            MyScreenDecals.OnResourcesRequesting();
        }

        internal static void GatherTextures()
        {
            MyMeshMaterials1.OnResourcesGathering();
            MyVoxelMaterials1.OnResourcesGather();
        }

        #endregion

        #region Fonts

        static SortedDictionary<int, MyRenderFont> m_fontsById = new SortedDictionary<int, MyRenderFont>();
        static MyRenderFont m_debugFont;
        internal static MyRenderFont DebugFont { get { return m_debugFont; } }

        internal static void AddFont(int id, MyRenderFont font, bool isDebugFont)
        {
            Debug.Assert(!m_fontsById.ContainsKey(id), "Adding font with ID that already exists.");
            if (isDebugFont)
            {
                Debug.Assert(m_debugFont == null, "Debug font was already specified and it will be overwritten.");
                m_debugFont = font;
            }
            m_fontsById[id] = font;
        }

        internal static MyRenderFont GetDebugFont()
        {
            return m_debugFont;
        }

        internal static MyRenderFont GetFont(int id)
        {
            return m_fontsById[id];
        }

        #endregion

        private static MyUnorderedAccessTexture m_reduce0;
        private static MyUnorderedAccessTexture m_reduce1;
        private static MyUnorderedAccessTexture m_uav3;
        private static MyUnorderedAccessTexture m_prevLum;

        internal static MyUnorderedAccessTexture HalfScreenUavHDR;
        internal static MyUnorderedAccessTexture QuarterScreenUavHDR;
        internal static MyUnorderedAccessTexture EighthScreenUavHDR;
        internal static MyUnorderedAccessTexture EighthScreenUavHDRHelper;

        internal static MyUnorderedAccessTexture m_rgba8_linear;
        internal static MyCustomTexture m_rgba8_0;
        internal static MyRenderTarget m_rgba8_1;
        internal static MyRenderTarget m_rgba8_2;
        internal static MyRenderTarget m_rgba8_ms;

        internal static RwTexId PostProcessedShadows = RwTexId.NULL;
        internal static RwTexId CascadesHelper = RwTexId.NULL;
        internal static RwTexId m_gbuffer1Copy = RwTexId.NULL;

        internal static void RemoveScreenResources()
        {
            if (m_reduce0 != null)
            {
                m_reduce0.Release();
                m_reduce1.Release();
                m_uav3.Release();
                HalfScreenUavHDR.Release();
                QuarterScreenUavHDR.Release();
                EighthScreenUavHDR.Release();
                EighthScreenUavHDRHelper.Release();
                m_rgba8_linear.Release();
                m_rgba8_0.Release();
                m_rgba8_1.Release();
                m_rgba8_2.Release();
                if (m_rgba8_ms != null)
                {
                    m_rgba8_ms.Release();
                    m_rgba8_ms = null;
                }
                m_prevLum.Release();

                MyRwTextures.Destroy(ref PostProcessedShadows);
                MyRwTextures.Destroy(ref CascadesHelper);
                MyRwTextures.Destroy(ref m_gbuffer1Copy);
            }
        }

        internal static void CreateScreenResources()
        {
            var width = m_resolution.X;
            var height = m_resolution.Y;
            var samples = RenderSettings.AntialiasingMode.SamplesCount();

            MyUtils.Init(ref MyGBuffer.Main);
            MyGBuffer.Main.Resize(width, height, samples, 0);

            MyScreenDependants.Resize(width, height, samples, 0);

            RemoveScreenResources();

            m_reduce0 = new MyUnorderedAccessTexture(width, height, Format.R32G32_Float);
            m_reduce0.SetDebugName("reduce0");
            m_reduce1 = new MyUnorderedAccessTexture(width, height, Format.R32G32_Float);
            m_reduce1.SetDebugName("reduce1");
            m_uav3 = new MyUnorderedAccessTexture(width, height, MyGBuffer.LBufferFormat);

            HalfScreenUavHDR = new MyUnorderedAccessTexture(width / 2, height / 2, MyGBuffer.LBufferFormat);
            QuarterScreenUavHDR = new MyUnorderedAccessTexture(width / 4, height / 4, MyGBuffer.LBufferFormat);
            EighthScreenUavHDR = new MyUnorderedAccessTexture(width / 8, height / 8, MyGBuffer.LBufferFormat);
            EighthScreenUavHDRHelper = new MyUnorderedAccessTexture(width / 8, height / 8, MyGBuffer.LBufferFormat);

            m_rgba8_linear = new MyUnorderedAccessTexture(width, height, Format.R8G8B8A8_UNorm);

            m_rgba8_0 = new MyCustomTexture(width, height, BindFlags.RenderTarget | BindFlags.ShaderResource, Format.R8G8B8A8_Typeless);
            m_rgba8_0.AddView(new MyViewKey { Fmt = Format.R8G8B8A8_UNorm, View = MyViewEnum.RtvView });
            m_rgba8_0.AddView(new MyViewKey { Fmt = Format.R8G8B8A8_UNorm_SRgb, View = MyViewEnum.RtvView });
            m_rgba8_0.AddView(new MyViewKey { Fmt = Format.R8G8B8A8_UNorm_SRgb, View = MyViewEnum.SrvView });

            m_rgba8_1 = new MyRenderTarget(width, height, Format.R8G8B8A8_UNorm_SRgb, 1, 0);
            m_rgba8_2 = new MyRenderTarget(width, height, Format.R8G8B8A8_UNorm_SRgb, 1, 0);
            if (samples > 1)
            {
                m_rgba8_ms = new MyRenderTarget(width, height, Format.R8G8B8A8_UNorm_SRgb, samples, 0);
            }
            m_prevLum = new MyUnorderedAccessTexture(1, 1, Format.R32G32_Float);

            Debug.Assert(PostProcessedShadows == RwTexId.NULL);
            Debug.Assert(CascadesHelper == RwTexId.NULL);
            PostProcessedShadows = MyRwTextures.CreateUavRenderTarget(width, height, Format.R8_UNorm);
            CascadesHelper = MyRwTextures.CreateRenderTarget(width, height, Format.R8_UNorm);

            m_gbuffer1Copy = MyRwTextures.CreateScratch2D(width, height, Format.R8G8B8A8_UNorm, samples, 0, "gbuffer 1 copy");
        }

        internal static void CopyGbufferToScratch()
        {
            MyImmediateRC.RC.DeviceContext.CopyResource(MyGBuffer.Main.m_resources[(int)MyGbufferSlot.GBuffer1].m_resource, m_gbuffer1Copy.Resource);
        }
    }
}
