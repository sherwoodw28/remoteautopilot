using OWML.Common;
using OWML.ModHelper;
using OWML.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Reflection;

namespace ShipRemoteAutopilot
{
    public class ShipRemoteAutopilot : ModBehaviour
    {
        //OPTION STRUCT
        private readonly struct TravelOption
        {
            public AstroObject.Name AstroObjectName { get; }
            public string DisplayName { get; }
            public Key MainKeyboardKey { get; }
            public Key NumpadKey { get; }

            public TravelOption(AstroObject.Name astroObjectName, string displayName, Key mainKeyboardKey, Key numpadKey)
            {
                AstroObjectName = astroObjectName;
                DisplayName = displayName;
                MainKeyboardKey = mainKeyboardKey;
                NumpadKey = numpadKey;
            }
        }


        bool keysMode;
        bool thrustersFiring = false;
        private bool landingActive = false;

        //CONSTANTS
        private const float maxLandingSpeed = 30f;
        private const float touchdownSpeed = 2f;
        private const float landingBrakeRate = 2f;
        private const float fallbackLandingSpeed = 10f;

        //OBJECTS
        private ShipBody shipBody;
        private OWRigidbody shipRigidbody;
        private Autopilot autopilot;
        private ThrusterModel thrusters;
        private AlignShipWithReferenceFrame shipAlignment;
        private LandingPadManager landingManager;
        private FluidDetector shipFluidDetector;
        private AstroObject planetaryBody;
        private AstroObject activeDestination;

        //DESTINATIONS
        private int selectedTravelOptionIndex = -1;
        
        private static readonly TravelOption[] travelOptions =
        {
            new TravelOption(AstroObject.Name.TimberHearth, "Timber Hearth", Key.Digit1, Key.Numpad1),
            new TravelOption(AstroObject.Name.TimberMoon, "The Attlerock", Key.Digit2, Key.Numpad2),
            new TravelOption(AstroObject.Name.GiantsDeep, "Giant's Deep", Key.Digit3, Key.Numpad3),
            new TravelOption(AstroObject.Name.BrittleHollow, "Brittle Hollow", Key.Digit4, Key.Numpad4),
            new TravelOption(AstroObject.Name.TowerTwin, "Ash Twin", Key.Digit5, Key.Numpad5),
            new TravelOption(AstroObject.Name.CaveTwin, "Ember Twin", Key.Digit6, Key.Numpad6),
            new TravelOption(AstroObject.Name.Comet, "The Interloper", Key.Digit7, Key.Numpad7),
            new TravelOption(AstroObject.Name.DarkBramble, "Dark Bramble", Key.Digit8, Key.Numpad8),
        };



        //HELPER AND MAIN FUNCTIONS
        public void NotifyHUD(string message)
        {
            //Clear old notifications.
            NotificationManager.SharedInstance.ClearAllNotifications();

            var notificationDataPlayer = new NotificationData(
                NotificationTarget.Player,
                message,
                2f,
                true
            );

            var notificationDataShip = new NotificationData(
            NotificationTarget.Ship,
            message,
            2f,
            true
            );

            NotificationManager.SharedInstance.PostNotification(notificationDataPlayer, false);
            NotificationManager.SharedInstance.PostNotification(notificationDataShip, false);
        }

        public void TravelToLocation()
        {
            var currentRefFrame = activeDestination.GetOWRigidbody().GetReferenceFrame();
            var tracker = Locator.GetPlayerTransform().GetComponent<ReferenceFrameTracker>();
            tracker.TargetReferenceFrame(currentRefFrame);
            autopilot.FlyToDestination(currentRefFrame);
        }

        private void SetDestination(int index)
        {
            if (index < 0 || index >= travelOptions.Length)
                return;

            selectedTravelOptionIndex = index;

            TravelOption option = travelOptions[selectedTravelOptionIndex];
            planetaryBody = Locator.GetAstroObject(option.AstroObjectName);

            NotifyHUD($"Autopilot Locked: {option.DisplayName}");
        }

        private void AbortRemoteAutopilot()
        {
            StopAllCoroutines();
            thrustersFiring = false;
            landingActive = false;

            autopilot.Abort();
            autopilot.StopMatchVelocity();

            GlobalMessenger.FireEvent("ExitLandingMode");

            activeDestination = null;
            NotifyHUD("Autopilot Abort.");
        }

        private IEnumerator FireUpwardThrusters()
        {
            thrustersFiring = true;
            yield return new WaitForSeconds(10f);
            thrustersFiring = false;

            TravelToLocation();
        }




        //INPUT MANAGEMENT
        private void HandleDestinationInput()
        {
            int keyboardSelection =
                GetKeyboardTravelOptionInput();

            if (keyboardSelection >= 0)
            {
                SetDestination(keyboardSelection);
                return;
            }

            HandleControllerDestinationInput();
        }

        private void CycleTravelOption(int direction)
        {
            if (travelOptions.Length == 0)
                return;

            if (selectedTravelOptionIndex < 0)
            {
                selectedTravelOptionIndex =
                    direction > 0
                        ? 0
                        : travelOptions.Length - 1;
            }
            else
            {
                selectedTravelOptionIndex += direction;

                if (selectedTravelOptionIndex >= travelOptions.Length)
                {
                    selectedTravelOptionIndex = 0;
                }
                else if (selectedTravelOptionIndex < 0)
                {
                    selectedTravelOptionIndex =
                        travelOptions.Length - 1;
                }
            }

            SetDestination(selectedTravelOptionIndex);
        }

        private bool EngagePressed()
        {
            return KeyboardEngagePressed()
                || ControllerEngagePressed();
        }

        private bool AbortPressed()
        {
            return KeyboardAbortPressed()
                || ControllerAbortPressed();
        }

        //KEYBOARD
        private bool KeyboardEngagePressed()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return false;

            return keysMode
                ? keyboard.enterKey.wasPressedThisFrame
                : keyboard.numpadEnterKey.wasPressedThisFrame;
        }

        private bool KeyboardAbortPressed()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return false;

            return keyboard.digit0Key.wasPressedThisFrame
                || keyboard.numpad0Key.wasPressedThisFrame;
        }

        private int GetKeyboardTravelOptionInput()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return -1;

            for (int i = 0; i < travelOptions.Length; i++)
            {
                TravelOption option = travelOptions[i];

                Key key = keysMode ? option.MainKeyboardKey : option.NumpadKey;

                if (keyboard[key].wasPressedThisFrame)
                {
                    return i;
                }
            }

            return -1;
        }


        //GAMEPAD
        private bool ControllerModifierHeld()
        {
            Gamepad gamepad = Gamepad.current;

            if (gamepad == null)
                return false;

            return gamepad.leftShoulder.isPressed;
        }

        private bool ControllerEngagePressed()
        {
            Gamepad gamepad = Gamepad.current;

            return gamepad != null
                && ControllerModifierHeld()
                && gamepad.dpad.up.wasPressedThisFrame;
        }

        private bool ControllerAbortPressed()
        {
            Gamepad gamepad = Gamepad.current;

            return gamepad != null
                && ControllerModifierHeld()
                && gamepad.dpad.down.wasPressedThisFrame;
        }

        private void HandleControllerDestinationInput()
        {
            Gamepad gamepad = Gamepad.current;

            if (gamepad == null || !ControllerModifierHeld())
                return;

            if (gamepad.dpad.right.wasPressedThisFrame)
            {
                CycleTravelOption(1);
            }
            else if (gamepad.dpad.left.wasPressedThisFrame)
            {
                CycleTravelOption(-1);
            }
        }



        //LANDING AND DESCENT
        private void UpdateLandingThrusters()
        {
            var planetRigidbody = activeDestination.GetOWRigidbody();
            var referenceFrame = planetRigidbody.GetReferenceFrame();

            //Where is the ship?
            Vector3 shipPosition = shipRigidbody.GetWorldCenterOfMass();

            //What direction is directly down toward the planet?
            Vector3 downDirection = (referenceFrame.GetPosition() - shipPosition).normalized;

            //How fast is the planet moving at the point underneath the ship?
            Vector3 surfaceVelocity = planetRigidbody.GetPointVelocity(shipPosition);

            float surfaceDistance = GetSurfaceDistance();
            float landingSpeed = activeDestination == Locator.GetAstroObject(AstroObject.Name.GiantsDeep) ? maxLandingSpeed : fallbackLandingSpeed;

            if (surfaceDistance >= 0f)
            {
                landingSpeed = GetDesiredLandingSpeed(surfaceDistance);
            }

            //We want to move exactly with the surface,
            Vector3 desiredVelocity = surfaceVelocity + (downDirection * landingSpeed);

            //What change in velocity do we currently need?
            Vector3 velocityError = desiredVelocity - shipRigidbody.GetVelocity();

            //How much velocity can the ship's thrusters correct this physics tick?
            float maxThrust = thrusters.GetMaxTranslationalThrust();
            float maxVelocityChange = maxThrust * Time.fixedDeltaTime;

            float inputMagnitude = Mathf.Min(1f, velocityError.magnitude / maxVelocityChange);

            Vector3 worldInput = Vector3.zero;

            if (velocityError.sqrMagnitude > 0.0001f)
            {
                worldInput = velocityError.normalized * inputMagnitude;
            }

            //ThrusterModel wants ship-local input, not a world-space direction.
            Vector3 localInput =
                shipRigidbody.transform.InverseTransformDirection(worldInput);

            //Actually operate the ship's thrusters.
            thrusters.AddTranslationalInput(localInput);
        }

        private void BeginLanding()
        {
            var currentRefFrame = activeDestination.GetOWRigidbody().GetReferenceFrame();

            GlobalMessenger<ReferenceFrame>.FireEvent("EnterLandingMode", currentRefFrame);
            landingActive = true;
        }

        private void EndLanding()
        {
            landingActive = false;
            GlobalMessenger.FireEvent("ExitLandingMode");

            activeDestination = null;

            NotifyHUD("Ship Landed on " + travelOptions[selectedTravelOptionIndex].DisplayName);
        }

        private float GetDesiredLandingSpeed(float surfaceDistance)
        {
            float desiredSpeed = Mathf.Sqrt(
                (touchdownSpeed * touchdownSpeed)
                + (2f * landingBrakeRate * surfaceDistance)
            );

            return Mathf.Min(desiredSpeed, maxLandingSpeed);
        }

        private float GetSurfaceDistance()
        {
            if (activeDestination == null)
                return -1f;

            OWRigidbody planetBody = activeDestination.GetOWRigidbody();
            Vector3 shipPosition = shipRigidbody.GetWorldCenterOfMass();
            Vector3 downDirection = (planetBody.GetWorldCenterOfMass() - shipPosition).normalized;
            float maxScanDistance = Vector3.Distance(shipPosition, planetBody.GetWorldCenterOfMass());

            RaycastHit hit;


            if (Physics.Raycast(shipPosition,downDirection,out hit,maxScanDistance,OWLayerMask.physicalMask,QueryTriggerInteraction.Ignore))
            {
                return hit.distance;
            }

            return -1f;
        }

        private void AwkwardLanding()
        {
            landingActive = false;

            autopilot.Abort();
            autopilot.StopMatchVelocity();

            GlobalMessenger.FireEvent("ExitLandingMode");

            activeDestination = null;

            NotifyHUD("Non-Optimal Landing Occured.");
        }

        private void OnShipArrived(float arrivalError)
        {
            NotifyHUD("Ship in Orbit. Descending...");
            BeginLanding();
        }


        //Base Unity Functions
        private void Start()
        {
            //Whenever the scene completely loads.
            LoadManager.OnCompleteSceneLoad += (scene, loadScene) =>
            {
                //Do nothing if the scene is not the main solar system.
                if (loadScene != OWScene.SolarSystem) return;

                //Defines all the fun things we get to work with.
                shipBody = FindObjectOfType<ShipBody>();
                autopilot = shipBody.GetComponent<Autopilot>();
                thrusters = shipBody.GetComponent<ThrusterModel>();
                shipRigidbody = shipBody.GetComponent<OWRigidbody>();
                shipAlignment = shipBody.GetComponent<AlignShipWithReferenceFrame>();
                landingManager = shipBody.GetComponent<LandingPadManager>();
                shipFluidDetector = shipRigidbody.GetAttachedFluidDetector();


                //Assigns that to that.
                autopilot.OnArriveAtDestination += OnShipArrived;

                //Chooses which set of keys to use.
                keysMode = ModHelper.Config.GetSettingsValue<bool>("Alternate Keys Mode");
            };
        }

        private void FixedUpdate()
        {
            //Ignore all of this if not landing.
            if (!landingActive)
                return;

            //Giant's Deep special case.
            if (activeDestination == Locator.GetAstroObject(AstroObject.Name.GiantsDeep) &&
                shipFluidDetector != null &&
                shipFluidDetector.InFluidType(FluidVolume.Type.WATER))
            {
                EndLanding();
                return;
            }

            //Is landed... stop trying to land.
            if (landingManager.IsLanded())
            {
                EndLanding();
                return;
            }

            //At least one landing pad has touched something.
            if (landingManager.GetContactCount() > 0)
            {
                AwkwardLanding();
                return;
            }


            UpdateLandingThrusters();
        }

        private void Update()
        {
            //Are we aborting all actions?
            if (AbortPressed())
            {
                AbortRemoteAutopilot();
                return;
            }

            if (Gamepad.current != null && !Gamepad.current.leftShoulder.wasPressedThisFrame)
            {
                NotifyHUD("REMOTE AUTOPILOT IS ACTIVE.");
            }

            //Handle any potential inputs for the mod.
            HandleDestinationInput();

            //Are the upwards thrusters firing?
            if (thrustersFiring)
            {
                thrusters.AddTranslationalInput(Vector3.up);
            }

            //Activation key for the Remote Autopilot function.
            if (EngagePressed() && !thrustersFiring)
            {
                if (planetaryBody == null)
                {
                    NotifyHUD("ERROR: No destination selected.");
                    return;
                }

                activeDestination = planetaryBody;

                //Only fire upwards if the ship is upright, otherwise... currently just randomly attempt to fly.
                if (Locator.GetShipBody().GetComponent<LandingPadManager>().IsLanded())
                {
                    NotifyHUD("Auto-Launch from " + travelOptions[selectedTravelOptionIndex].DisplayName);
                    StartCoroutine(FireUpwardThrusters());
                }
                else
                {
                    NotifyHUD("Non-Launch Takeoff from " + travelOptions[selectedTravelOptionIndex].DisplayName);
                    TravelToLocation();
                }
            }
        }
    }
}