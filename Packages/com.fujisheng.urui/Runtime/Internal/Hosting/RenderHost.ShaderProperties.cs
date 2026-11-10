using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Shaders;

namespace URUI.Internal.Hosting
{
    // 按 shader 契约写入属性并录制单项或批次绘制，避免共享属性块残留上次状态。
    internal sealed partial class RenderHost
    {
        static readonly int[] paletteTextureIds=PaletteIds("_UruiTexture");
        static readonly int[] paletteSourceIds=PaletteIds("_UruiSource");
        static int[] PaletteIds(string prefix)
        {
            var ids=new int[TextureBindingBudget-1];
            for(var index=0;index<ids.Length;index++)ids[index]=Shader.PropertyToID(prefix+(index+1));
            return ids;
        }
        void WriteShaderProperties(MaterialPropertyBlock properties, Texture texture, CommandNative cmd, bool batch, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            using var marker = propertiesMarker.Auto();
#endif
            TextureShaderState.Apply(properties, texture, cmd.texture);
            var clip = new Vector4(cmd.clip_x, cmd.clip_y, cmd.clip_x + cmd.clip_w, cmd.clip_y + cmd.clip_h);
            var translation = batch ? Vector4.zero : new Vector4(cmd.tx, cmd.ty, 0, 0);
            properties.SetTexture(mainTextureId, texture);
            properties.SetFloat(clipEnabledId, cmd.ClipEnabled ? 1 : 0);
            properties.SetFloat(textureSamplingId, cmd.TextureSampling);
            properties.SetVector(clipRectId, clip);
            properties.SetVector(translationId, translation);
        }

        MaterialPropertyBlock PrepareShaderProperties(Texture texture, CommandNative cmd, bool batch, ref PhaseFrame phase)
        {
            var properties = reusableProperties ??= new MaterialPropertyBlock();
#if URUI_DEBUG
            if (propertyDependencyDiagnostics)
            {
                propertyDependencies.Observe(properties, texture, cmd, batch);
            }

            PropertyCalls++;
#endif
            if (shaderPropertyState.NeedsWrite(properties, texture, cmd, ShaderContractActive, material))
            {
                properties.Clear();

                WriteShaderProperties(properties, texture, cmd, batch, ref phase);
#if URUI_DEBUG
                PropertyWriteCalls++;
#endif
            }
#if URUI_DEBUG
            else
            {
                PropertyReuseHits++;
            }
#endif
            return properties;
        }

        void DrawOne(BatchItem item, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            var cmd = item.command;
            var tex = LoadTexture(cmd.texture);
            // 实时 Unity 输出保留线性混合契约；标准图片和 CSS 几何使用编码域。
            SelectEncodedRaster(tex);
            var properties = PrepareShaderProperties(tex ? tex : white, cmd, false, ref phase);
            var m = item.transform * Matrix4x4.Translate(new Vector3(cmd.tx, cmd.ty, 0));
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.propertiesMs += Millis(Now() - t);
                t = Now();
            }

            if (!skipDrawForDiagnosis)
#endif
            {
#if URUI_DEBUG
                using (submitMarker.Auto())
#endif
                {
                    buffer.DrawMesh(SourceMesh(item.geometry), m, material, 0, 0, properties);
                    RecordedGeometryDraws++;
                    CaptureOrdinaryDraw(SourceMesh(item.geometry),m,cmd,false);
#if URUI_DEBUG
                    recordCallsThisFrame++;
#endif
                }
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.submitMs += Millis(Now() - t);
                if (!skipDrawForDiagnosis)
                {
                    phase.drawSubmissions++;
                }
            }
#endif
        }

        void DrawBatch(BatchCache cached, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            var cmd = batchGroup[0].command;
            var tex = LoadTexture(cmd.texture);
            SelectEncodedRaster(tex);
            var properties = PrepareShaderProperties(tex ? tex : white, cmd, true, ref phase);
            for(var index=1;index<batchTextureCount;index++)
            {
                var source=LoadTexture(batchTextures[index]);if(!source)source=white;
                properties.SetTexture(paletteTextureIds[index-1],source);
                properties.SetVector(paletteSourceIds[index-1],TextureShaderState.SourceInfo(source,batchTextures[index]));
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.propertiesMs += Millis(Now() - t);
                t = Now();
            }

            if (!skipDrawForDiagnosis)
#endif
            {
#if URUI_DEBUG
                using (submitMarker.Auto())
#endif
                {
                    buffer.DrawMesh(cached.mesh, batchGroup[0].transform, material, 0, 0, properties);
                    RecordedGeometryDraws++;
                    CaptureOrdinaryDraw(cached.mesh,batchGroup[0].transform,cmd,true);
#if URUI_DEBUG
                    recordCallsThisFrame++;
#endif
                }
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.submitMs += Millis(Now() - t);
                if (!skipDrawForDiagnosis)
                {
                    phase.drawSubmissions++;
                }
            }
#endif
        }
    }
}
