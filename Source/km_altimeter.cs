/*
 * Author: dtobi, Firov
 * This work is shared under CC BY-NC-ND 3.0 license.
 * Non commercial, no derivatives, attribution if shared unmodified.
 * You may distribute this code and the compiled .dll as is.
 * 
 * Exception from the no-deriviates clause in case of KSP updates:
 * In case of an update of KSP that breaks the code, you may change
 * this code to make it work again and redistribute it under a different
 * class name until the author publishes an updated version. After a
 * release by the author, the right to redistribute the changed code
 * vanishes.
 * 
 * You must keep this boilerplate in the file and give credit to the author
 * in the download file as well as on the webiste that links to the file.
 * 
 * Should you wish to change things in the code, contact me via the KSP forum.
 * Patches are welcome.
 *
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;
using KSPAPIExtensions;

namespace KM_Lib
{
    public class KM_Altimeter : PartModule
    {

        #region Fields

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Group"),
            UI_ChooseOption(
            options = new String[] {
                "0",
                "1",
                "2",
                "3",
                "4",
                "5",
                "6",
                "7",
                "8",
                "9",
                "10",
                "11",
                "12",
                "13",
                "14",
                "15"
            },
            display = new String[] {
                "Stage",
                "AG1",
                "AG2",
                "AG3",
                "AG4",
                "AG5",
                "AG6",
                "AG7",
                "AG8",
                "AG9",
                "AG10",
                "Lights",
                "RCS",
                "SAS",
                "Brakes",
                "Abort"
            }
        )]
        public string group = "0";

        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Meters", guiFormat = "F0", guiUnits = "m"),
            UI_FloatEdit(scene = UI_Scene.All, minValue = 0f, maxValue = 1000f, incrementLarge = 200f, incrementSmall = 25f, incrementSlide = 1f)]
        public float meterHeight = 0;

        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Kilometers", guiFormat = "F0", guiUnits = "km"),
            UI_FloatEdit(scene = UI_Scene.All, minValue = 0f, maxValue = 500f, incrementLarge = 100f, incrementSmall = 25f, incrementSlide = 1f)]
        public float kilometerHeight = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Detection"),
            UI_Toggle(disabledText = "Disabled", enabledText = "Enabled")]
        public bool isArmed = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Trigger on"),
            UI_ChooseOption(options = new string[] { "All", "Ascent", "Descent" })]
        public string direction = "All";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Auto Reset"),
            UI_Toggle(disabledText = "False", enabledText = "True")]
        public bool autoReset = false;

        [KSPField(isPersistant = true)]
        public bool useKilometer = false;

        #endregion


        #region Events

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Use Kilometers")]
        public void useKilometers() {
            useKilometer = true;
            updateButtons();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Use Meters")]
        public void useMeters() {
            useKilometer = false;
            updateButtons();
        }

        [KSPAction("Activate Detection")]
        public void doActivateAG(KSPActionParam param) {
            isArmed = true;
        }

        [KSPAction("Deactivate Detection")]
        public void doDeActivateAG(KSPActionParam param) {
            isArmed = false;
        }

        #endregion


        #region Variables

        private double alt = 0;
        private double currentWindow = 0;
        private Boolean illuminated = false;
        private Boolean ascending = false;
        private Boolean fireNextupdate = false;

        #endregion


        #region Overrides

        public override void OnStart(StartState state) {
            if (state == StartState.Editor) {
                this.part.OnEditorAttach += OnEditorAttach;
                this.part.OnEditorDetach += OnEditorDetach;
                this.part.OnEditorDestroy += OnEditorDestroy;
                OnEditorAttach();
            }
            //Initial button layout
            updateButtons();
            //Force activation no matter which stage it's on
            this.part.force_activate();
            print("KM Altimeter Detector Started");
        }

        public override void OnUpdate() {
            //Check to see if the device has been rearmed, if so, deactivate the lights
            if (isArmed && illuminated) {
                lightsOff();
            }
            //In order for physics to take effect on jettisoned parts, the staging event has to be fired from OnUpdate
            if (fireNextupdate) {
                km_Helper.fireEvent(this.part, int.Parse(group));
                fireNextupdate = false;
            }
        }

        public override void OnFixedUpdate() {
            //Check current altitude
            updateAltitude();

            //If the device is armed, check for the trigger altitude
            if (isArmed) {
                //We're ascending. Trigger at or above target height
                if (direction != "Descent" && ascending && Math.Abs((alt - currentWindow) - (useKilometer ? kilometerHeight * 1000 : meterHeight)) < currentWindow) {
                    //This flag is checked for in OnUpdate to trigger staging
                    fireNextupdate = true;
                    lightsOn();
                    isArmed = false;
                }
                //We're descending. Trigger at or below target height
                else if (direction != "Ascent" && !ascending && Math.Abs((alt + currentWindow) - (useKilometer ? kilometerHeight * 1000 : meterHeight)) < currentWindow) {
                    //This flag is checked for in OnUpdate to trigger staging
                    fireNextupdate = true;
                    lightsOn();
                    isArmed = false;
                }
            }

            //If auto reset is enabled, wait for departure from the target window and rearm
            if (!isArmed & autoReset) {
                if (ascending && Math.Abs((alt - currentWindow) - (useKilometer ? kilometerHeight * 1000 : meterHeight)) > currentWindow) {
                    isArmed = true;
                }
                else if (!ascending && Math.Abs((alt + currentWindow) - (useKilometer ? kilometerHeight * 1000 : meterHeight)) > currentWindow) {
                    isArmed = true;
                }
            }
        }

        #endregion


        #region Methods

        private void updateAltitude() {
            //Sea altitude
            double altSea = this.vessel.mainBody.GetAltitude(this.vessel.CoM);
            //Altitude over terrain. Does not factor in ocean surface.
            double altSurface = altSea - this.vessel.terrainAltitude;
            //Set the last altitude for the purpose of direction determination
            double lastAlt = alt;
            //Use the lowest of the two values as the current altitude.
            alt = (altSurface < altSea ? altSurface : altSea);
            //Determine if the vessel is ascending or descending
            ascending = (lastAlt < alt ? true : false);
            //Update target window size based on current vertical velocity
            currentWindow = Math.Abs((TimeWarp.fixedDeltaTime * this.vessel.verticalSpeed) * 1.05);
        }

        private void lightsOn() {
            //Switch off model lights
            Utility.switchLight(this.part, "light-go", true);
            Utility.playAnimationSetToPosition(this.part, "glow", 1);
            illuminated = true;
        }

        private void lightsOff() {
            //Switch off model lights
            Utility.switchLight(this.part, "light-go", false);
            Utility.playAnimationSetToPosition(this.part, "glow", 0);
            illuminated = false;
        }

        private void updateButtons() {
            if (useKilometer) {
                //Show meter button
                Events["useMeters"].guiActiveEditor = true;
                Events["useMeters"].guiActive = true;
                //Hide meter scale
                Fields["meterHeight"].guiActiveEditor = false;
                Fields["meterHeight"].guiActive = false;
                //Hide kilometer button
                Events["useKilometers"].guiActiveEditor = false;
                Events["useKilometers"].guiActive = false;
                //Show kilometer scale
                Fields["kilometerHeight"].guiActiveEditor = true;
                Fields["kilometerHeight"].guiActive = true;
                //Reset meter scale
                meterHeight = 0;
            }
            else {
                //Hide meter button
                Events["useMeters"].guiActiveEditor = false;
                Events["useMeters"].guiActive = false;
                //Show meter scale
                Fields["meterHeight"].guiActiveEditor = true;
                Fields["meterHeight"].guiActive = true;
                //Show kilometer button
                Events["useKilometers"].guiActiveEditor = true;
                Events["useKilometers"].guiActive = true;
                //Hide kilometer scale
                Fields["kilometerHeight"].guiActiveEditor = false;
                Fields["kilometerHeight"].guiActive = false;
                //Reset kilometer scale
                kilometerHeight = 0;
            }
        }

        private void OnEditorAttach() {
            RenderingManager.AddToPostDrawQueue(99, updateEditor);
        }

        private void OnEditorDetach() {
            RenderingManager.RemoveFromPostDrawQueue(99, updateEditor);
        }

        private void OnEditorDestroy() {
            RenderingManager.RemoveFromPostDrawQueue(99, updateEditor);

        }

        private void updateEditor() {
            //Update buttons
            updateButtons();
        }

        #endregion
    }
}

