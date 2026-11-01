using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    // 按 shader 契约写入属性并录制单项或批次绘制，避免共享属性块残留上次状态。
    internal sealed partial class RenderHost
    {
        void WriteShaderProperties(MaterialPropertyBlock properties, Texture texture, CommandNative cmd, bool batch, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            using var marker = propertiesMarker.Auto();
#endif
            var clip = new Vector4(cmd.clip_x, cmd.clip_y, cmd.clip_x + cmd.clip_w, cmd.clip_y + cmd.clip_h);
            var translation = batch ? Vector4.zero : new Vector4(cmd.tx, cmd.ty, 0, 0);
            if (useShaderPropertyIds)
            {
                properties.SetTexture(mainTextureId, texture);
                properties.SetFloat(clipEnabledId, cmd.clip_enabled);
                properties.SetVector(clipRectId, clip);
                properties.SetVector(translationId, translation);
            }
            else
            {
                properties.SetTexture("_MainTex", texture);
                properties.SetFloat("_UruiClipEnabled", cmd.clip_enabled);
                properties.SetVector("_UruiClipRect", clip);
                properties.SetVector("_UruiTranslation", translation);
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.shaderNameLookups += 4;
                }
#endif
            }
        }

        MaterialPropertyBlock PrepareShaderProperties(Texture texture, CommandNative cmd, bool batch, ref PhaseFrame phase)
        {
            if (reusePropertyBlock && reusableProperties == null)
            {
                reusableProperties = new MaterialPropertyBlock();
            }

            var properties = reusePropertyBlock ? reusableProperties : new MaterialPropertyBlock();
#if URUI_DEBUG
            if (!reusePropertyBlock && phaseDiagnostics)
            {
                phase.propertyBlocksCreated++;
            }

            if (propertyDependencyDiagnostics)
            {
                propertyDependencies.Observe(properties, texture, cmd, batch);
            }

            PropertyCalls++;
#endif
            if (shaderPropertyState.NeedsWrite(properties, texture, cmd, ShaderContractActive && reusePropertyBlock, material))
            {
                if (reusePropertyBlock)
                {
                    properties.Clear();
                }

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
                    buffer.DrawMesh(item.geometry.mesh, m, material, 0, 0, properties);
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
            var properties = PrepareShaderProperties(tex ? tex : white, cmd, true, ref phase);
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
