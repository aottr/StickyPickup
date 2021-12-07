/*
 * Copyright 2021 AlexOttr <alex@ottr.one>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, 
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to 
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY 
 * CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Enums;

namespace OttrOne.StickyPickup
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AlternativeColliderCheck : UdonSharpBehaviour
    {
        [Header("Alternative ColliderCheck for SyncedStickyPickup v1.2.1+")]
        [SerializeField, Tooltip("Root bone for the Sticky Pickup.")]
        private SyncedStickyPickup[] Pickups;

        [Header("Debug Options - Disable in productive world")]
        [SerializeField, Tooltip("Expose information to debug console.")]
        private bool DebugMode = false;

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            // run only for one player
            if (player != Networking.LocalPlayer) return;

            if (DebugMode) Debug.Log($"[StickyPickup] Player {player.displayName} exited Trigger. Perform Collider Check.");
            foreach (SyncedStickyPickup pickup in Pickups)
            {
                if (!player.IsOwner(pickup.gameObject)) return;
                if (DebugMode) Debug.Log($"[StickyPickup] Found {pickup.gameObject.name} on Player {player.displayName}. Perform Reset.");
                pickup.SendCustomEventDelayedSeconds("ResetPositionAllPlayers", 2.5F, EventTiming.Update);
            }
        }
    }
}

