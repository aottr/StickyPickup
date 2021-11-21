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
    public class SyncedStickyPickup : UdonSharpBehaviour
    {
        public HumanBodyBones Bone;
        public bool OnlyVR;
        public float Radius = 0.3f;

        private bool localStickedOn = false;
        private bool localPickedUp = false;

        private bool synchronize = false;

        [UdonSynced, FieldChangeCallback(nameof(DeattachLocal)), HideInInspector]
        public bool AttachedToPlayer = false;

        public bool DeattachLocal
        {
            set
            {
                localStickedOn = false;
            }
        }

        [UdonSynced, HideInInspector]
        public bool idle = true;

        [UdonSynced, HideInInspector]
        public bool isKinematic;
        private bool wasKinematic;

        private Rigidbody rigidBody;

        private Vector3 trackedPos;
        private Quaternion trackedRot;

        [UdonSynced, HideInInspector]
        public Vector3 objectPos;
        [UdonSynced, HideInInspector]
        public Quaternion objectRot;

        public void Start()
        {
            this.rigidBody = (Rigidbody)gameObject.GetComponent(typeof(Rigidbody));
            this.wasKinematic = rigidBody.isKinematic;
        }

        /// <summary>
        /// Called when the object gets picked up. It will set the owner to the perso who picked it up,
        /// set the global variables and disable the gravity effect
        /// </summary>
        public override void OnPickup()
        {
            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
            localStickedOn = false;
            AttachedToPlayer = false;
            idle = false;
            localPickedUp = true;

            // disable gravity effect if existing
            this.rigidBody.isKinematic = true;
            this.isKinematic = true;
            RequestSerialization();
        }

        /// <summary>
        /// Drop calculates the current offset to the selected bone. If its within the radius it assumes that the player wants to place
        /// the item there so it attaches it to the player.
        /// 
        /// If its outside the radius it will put it into idle state.
        /// </summary>
        public override void OnDrop()
        {
            localPickedUp = false;

            Vector3 objPos = this.rigidBody.transform.position;
            Vector3 plyPos = Networking.LocalPlayer.GetBonePosition(Bone);
            Quaternion invRot = Quaternion.Inverse(Networking.LocalPlayer.GetBoneRotation(Bone));

            // calculate 3d distance # TODO might change to Vector3.Distance at some point
            float offset = Mathf.Sqrt(Mathf.Pow(objPos.x - plyPos.x, 2) + Mathf.Pow(objPos.y - plyPos.y, 2) + Mathf.Pow(objPos.z - plyPos.z, 2));

            if (offset <= Radius && (Networking.LocalPlayer.IsUserInVR() || !OnlyVR))
            {
                AttachedToPlayer = true;
                localStickedOn = true;
                idle = false;

                // q^(-1) * Vector (x2-x1, y2-y1, z2-z1)
                trackedPos = invRot * (objPos - plyPos);
                // calculate the rotation by multiplying the current rotation with inverse player rotation
                trackedRot = invRot * this.rigidBody.transform.rotation;
            }
            else
            {
                idle = true;
                isKinematic = this.wasKinematic;
                this.rigidBody.isKinematic = this.wasKinematic;
            }
            RequestSerialization();
        }

        public override void PostLateUpdate()
        {
            bool changedTransform = false;
            if (localStickedOn)
            {
                Vector3 bonePosition = Networking.LocalPlayer.GetBonePosition(Bone);
                Quaternion boneRotation = Networking.LocalPlayer.GetBoneRotation(Bone);

                // calculate the wanted offset by multiplying the bonerotation with the position offset and add this to the targeted bone position
                Vector3 tmpPos = (boneRotation * trackedPos) + bonePosition;
                // calculate the wanted rotation by multiplying the current bone rotation with the calculated rotation offset 
                Quaternion tmpRot = boneRotation * trackedRot;

                // only set if changed
                if (Vector3.Distance(tmpPos, objectPos) > 0f)
                {
                    objectPos = tmpPos;
                    objectRot = tmpRot;
                    this.rigidBody.transform.SetPositionAndRotation(objectPos, objectRot);
                    changedTransform = true;
                }
            }
            else if (localPickedUp)
            {
                objectPos = this.rigidBody.transform.position;
                objectRot = this.rigidBody.transform.rotation;
                changedTransform = true;
            }
            // request Serialization for other players when item isnt idle and local player changed it
            if (!idle && (localStickedOn || localPickedUp) && changedTransform) RequestSerialization();
            if (synchronize)
            {
                this.rigidBody.transform.SetPositionAndRotation(objectPos, objectRot);
                synchronize = false;
            }
        }

        public override void OnDeserialization()
        {
            if (!idle && !localStickedOn && !localPickedUp) synchronize = true;
            if (this.rigidBody.isKinematic != isKinematic) this.rigidBody.isKinematic = isKinematic;
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            SendCustomEventDelayedSeconds("SyncLatePlayer", 2.5F, EventTiming.Update);
        }

        public void SyncLatePlayer()
        {
            RequestSerialization();
        }
    }
}
