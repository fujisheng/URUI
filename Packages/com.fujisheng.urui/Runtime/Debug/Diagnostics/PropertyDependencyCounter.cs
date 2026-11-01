#if URUI_DEBUG
using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Diagnostics
{
    // Diagnostic only: observes the exact submitted state. Never skips a Clear/write/draw.
    // Translation-independent hits are hypothetical until a shader contract is verified.
    internal sealed class PropertyDependencyCounter
    {
        MaterialPropertyBlock previousBlock;
        Texture previousTexture;
        CommandNative previous;
        int previousTx;
        int previousTy;
        bool valid;

        public int Calls
        {
            get;
            private set;
        }
        public int LegacyHits
        {
            get;
            private set;
        }
        public int TranslationIndependentHits
        {
            get;
            private set;
        }

        public void Reset()
        {
            valid = false;
            previousBlock = null;
            previousTexture = null;
            Calls = LegacyHits = TranslationIndependentHits = 0;
        }

        public void Observe(MaterialPropertyBlock block, Texture texture, CommandNative command, bool batch)
        {
            int tx = BitConverter.SingleToInt32Bits(batch ? 0 : command.tx);
            int ty = BitConverter.SingleToInt32Bits(batch ? 0 : command.ty);
            bool same = valid
                && ReferenceEquals(block, previousBlock)
                && ReferenceEquals(texture, previousTexture)
                && previous.clip_enabled == command.clip_enabled
                && previous.clip_x == command.clip_x
                && previous.clip_y == command.clip_y
                && previous.clip_w == command.clip_w
                && previous.clip_h == command.clip_h;
            Calls++;
            if (same)
            {
                TranslationIndependentHits++;
                if (tx == previousTx && ty == previousTy)
                {
                    LegacyHits++;
                }
            }

            valid = true;
            previousBlock = block;
            previousTexture = texture;
            previous = command;
            previousTx = tx;
            previousTy = ty;
        }
    }
}
#endif
