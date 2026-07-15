using System;
using System.Collections.Generic;
//using System.ComponentModel;
//using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;
using DataStructures;
using System.Threading;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using DataStructures.UtilityClasses;
using DataStructures.Database;
using DataStructures.Timing;
//using DataStructures;
//using NationalInstruments.Visa;
//using Ivi.Visa;

namespace WordGenerator
{
    public partial class RunForm : Form, SoftwareClockSubscriber
    {

        private DataStructures.Timing.ComputerClockSoftwareClockProvider softwareClockProvider;
        private DataStructures.Timing.NetworkClockProvider networkClockProvider;

        private SequenceData runningSequence = null;

        int repeatCount = 1;


        Thread getConfirmationThread;

        List<Socket> CameraPCsSocketList;
        List<SettingsData.IPAdresses> connectedPCs;

        bool isIdle = false;
        bool isCameraSaving = true;

        DateTime runStartTime;


        // this enum should enumerate the steps that should happen in sequence
        private enum RunFormStatus { Inactive, StartingRun, Running, FinishedRun, ClosableOnly };
        private RunFormStatus runFormStatus;

        private DateTime formCreationTime;

        private bool hasBeenActivated = false;

        // Added by Jere
        private bool serialConnected = false;
        private bool serial2Connected = false;//
        private bool serial3Connected = false;
        private bool serial4Connected = false;
        private bool serial5Connected = false;
        private bool serial6Connected = false;
        private bool serial7Connected = false;
        private bool serial8Connected = false; // added by GS and DC March

        private SerialPort _serialPort;
        private SerialPort _serialPort2;
        private SerialPort _serialPort3;
        private SerialPort _serialPort4;
        private SerialPort _serialPort5;
        private SerialPort _serialPort6;
        private SerialPort _serialPort7;
        private SerialPort _serialPort8;


        private string rs232cmd;
        private string rs232cmd2;
        private string rs232cmd3;
        private string rs232cmd4;
        private string rs232cmd5;
        private string rs232cmd6;
        private string rs232cmd7;
        private string rs232cmd8;

        private RS232GroupChannelData channelData;
        private RS232GroupChannelData channelData2;
        private RS232GroupChannelData channelData3;
        private RS232GroupChannelData channelData4;
        private RS232GroupChannelData channelData5;
        private RS232GroupChannelData channelData6;
        private RS232GroupChannelData channelData7;
        private RS232GroupChannelData channelData8;
        // End added by Jere (GS and DC March)

        public enum RunType
        {
            /// <summary>
            /// Run iteration #0 only.
            /// </summary>
            Run_Iteration_Zero,
            /// <summary>
            /// Run current iteration # only.
            /// </summary>
            Run_Current_Iteration,
            /// <summary>
            /// Run through the list of iteration #s in order.
            /// </summary>
            Run_Full_List,
            /// <summary>
            /// Run through the remaining list of iteration #s in order, starting with the current iteration #.
            /// </summary>
            Run_Continue_List,
            /// <summary>
            /// Runs the the full list, in random iteration order.
            /// </summary>
            Run_Random_Order_List
        }
        private RunType runType = RunType.Run_Iteration_Zero;

        private Thread runningThread = null;

        private bool runRepeat;

        private bool errorDetected;

        public bool ErrorDetected
        {
            get { return errorDetected; }
            set
            {
                bool temp = (errorDetected != value);
                errorDetected = value;
                if (temp)
                    updateErrorDisplay();
            }
        }


        private void updateErrorDisplay()
        {
            Action updateDisplayAction = () =>
            {
                if (errorDetected)
                {
                    textBox1.BackColor = Color.Red;
                }
                else
                {
                    textBox1.BackColor = this.BackColor;
                }
            };

            Invoke(updateDisplayAction);
        }

        private void setStatus(RunFormStatus status)
        {
            if (this.InvokeRequired)
            {
                Action<RunFormStatus> callback = setStatus;
                this.Invoke(callback, new object[] { status });
            }
            else
            {
                runFormStatus = status;

                switch (runFormStatus)
                {
                    case RunFormStatus.Inactive:
                        stopButton.Enabled = false;
                        closeButton.Enabled = false;
                        progressBar.Enabled = false;
                        runAgainButton.Enabled = false;
                        abortAfterThis.Enabled = false;
                        break;

                    case RunFormStatus.StartingRun:
                        stopButton.Enabled = true;
                        closeButton.Enabled = false;
                        progressBar.Enabled = false;
                        runAgainButton.Enabled = false;
                        abortAfterThis.Enabled = true;
                        break;

                    case RunFormStatus.FinishedRun:
                        stopButton.Enabled = false;
                        closeButton.Enabled = true;
                        progressBar.Enabled = false;
                        if ((this.runType == RunType.Run_Iteration_Zero || this.runType == RunType.Run_Current_Iteration) && !isIdle)
                        {
                            runAgainButton.Enabled = true;
                        }
                        abortAfterThis.Enabled = false;
                        if (userAborted && this.isBackgroundRunform)
                        {
                            this.Close();
                        }
                        break;

                    case RunFormStatus.Running:
                        stopButton.Enabled = true;
                        closeButton.Enabled = false;
                        progressBar.Enabled = true;
                        runAgainButton.Enabled = false;
                        abortAfterThis.Enabled = true;
                        break;

                    case RunFormStatus.ClosableOnly:
                        stopButton.Enabled = false;
                        closeButton.Enabled = true;
                        progressBar.Enabled = false;
                        runAgainButton.Enabled = false;
                        abortAfterThis.Enabled = false;
                        break;
                }
            }
        }

        private Dictionary<int, Button> hotkeyButtons;

        public RunForm(SequenceData sequenceToRun)
        {
            this.runningSequence = sequenceToRun;
            if (WordGenerator.MainClientForm.instance.studentEdition)
            {
                MessageBox.Show("Your Cicero Professional Edition (C) License expired on March 31. You are now running a temporary 24 hour STUDENT EDITION license. Please see http://web.mit.edu/~akeshet/www/Cicero/apr1.html for license renewal information.", "License expired -- temporary STUDENT EDITION license.");
            }

            IPAddress lclhst = null;
            IPEndPoint ipe = null;
            CameraPCsSocketList = new List<Socket>();
            connectedPCs = new List<SettingsData.IPAdresses>();
            bool errorOccured = false;
            foreach (SettingsData.IPAdresses ipAdress in Storage.settingsData.CameraPCs)
            {
                errorOccured = false;
                if (ipAdress.inUse)
                {
                    try
                    {

                        lclhst = Dns.GetHostEntry(ipAdress.pcAddress).AddressList[0];
                        ipe = new IPEndPoint(lclhst, ipAdress.Port);
                        CameraPCsSocketList.Add(new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp));

                        CameraPCsSocketList[CameraPCsSocketList.Count - 1].Blocking = false;
                        CameraPCsSocketList[CameraPCsSocketList.Count - 1].SendTimeout = 100;
                        CameraPCsSocketList[CameraPCsSocketList.Count - 1].ReceiveTimeout = 100;

                    }
                    catch
                    {
                        errorOccured = true;
                    }
                    if (errorOccured)
                        continue;
                    else
                    {
                        connectedPCs.Add(ipAdress);
                        try
                        {
                            CameraPCsSocketList[CameraPCsSocketList.Count - 1].Connect(ipe);
                        }
                        catch { }
                    }
                }
            }

            if (Storage.settingsData.CameraPCs.Count != 0)
            {
                getConfirmationThread = new Thread(new ThreadStart(getConfirmationEntryPoint));
                getConfirmationThread.Start();
            }
            // Supress hotkeys in main form when this form is runnings. This will be cleared when the run form closes.
            WordGenerator.MainClientForm.instance.suppressHotkeys = true;

            InitializeComponent();
            runFormStatus = RunFormStatus.Inactive;
            formCreationTime = DateTime.Now;

            // hotkey registration
            hotkeyButtons = new Dictionary<int, Button>();


            // fortune cookie
            if (WordGenerator.MainClientForm.instance.fortunes != null)
            {
                List<string> forts = WordGenerator.MainClientForm.instance.fortunes;
                Random rand = new Random();
                fortuneCookieLabel.Text = forts[rand.Next(forts.Count - 1)];
            }



        }

        private void registerAllHotkeys()
        {
            // Abort button
            RegisterHotKey(Handle, hotkeyButtons.Count, KeyModifiers.None, Keys.A);
            hotkeyButtons.Add(hotkeyButtons.Count, stopButton);

            RegisterHotKey(Handle, hotkeyButtons.Count, KeyModifiers.None, Keys.Escape);
            hotkeyButtons.Add(hotkeyButtons.Count, stopButton);

            // Run button (2 hotkeys)

            RegisterHotKey(Handle, hotkeyButtons.Count, KeyModifiers.None, Keys.R);
            hotkeyButtons.Add(hotkeyButtons.Count, runAgainButton);

            RegisterHotKey(Handle, hotkeyButtons.Count, KeyModifiers.None, Keys.F9);
            hotkeyButtons.Add(hotkeyButtons.Count, runAgainButton);

            // Close button

            RegisterHotKey(Handle, hotkeyButtons.Count, KeyModifiers.None, Keys.C);
            hotkeyButtons.Add(hotkeyButtons.Count, closeButton);

            RegisterHotKey(Handle, hotkeyButtons.Count, KeyModifiers.None, Keys.Space);
            hotkeyButtons.Add(hotkeyButtons.Count, closeButton);
        }

        private void unregisterAllHotkeys()
        {
            foreach (int id in hotkeyButtons.Keys)
            {
                UnregisterHotKey(Handle, id);
            }
            hotkeyButtons.Clear();
        }

        public RunForm(SequenceData sequenceToRun, RunType runType, bool runRepeat)
            : this(sequenceToRun)
        {
            this.runType = runType;
            this.runRepeat = runRepeat;
            if (runType == RunType.Run_Full_List || runType == RunType.Run_Continue_List)
                runRepeat = false;

            if (runType != RunType.Run_Current_Iteration && runType != RunType.Run_Iteration_Zero)
                abortAfterThis.Visible = true;


            if (runRepeat)
                abortAfterThis.Visible = true;
        }

        public RunForm(SequenceData sequenceToRun, RunType runType, bool runRepeat, bool isCameraSaving)
            : this(sequenceToRun)
        {
            this.runType = runType;

            this.isCameraSaving = isCameraSaving;
            this.savingWarning.Visible = !isCameraSaving;

            runningSequence.AISaved = isCameraSaving;

            this.runRepeat = runRepeat;

            if (runType == RunType.Run_Full_List || runType == RunType.Run_Continue_List)
                runRepeat = false;

            if (runType != RunType.Run_Current_Iteration && runType != RunType.Run_Iteration_Zero)
                abortAfterThis.Visible = true;


            if (runRepeat)
                abortAfterThis.Visible = true;
        }

        public void addMessageLogText(object sender, MessageEvent e)
        {


            if (this.InvokeRequired)
            {
                EventHandler<MessageEvent> ev = addMessageLogText;
                this.BeginInvoke(ev, new object[] { sender, e });
            }
            else
            {

                WordGenerator.MainClientForm.instance.handleMessageEvent(sender, e);
                MessageEvent message = (MessageEvent)e;
                if (!this.IsDisposed)
                {
                    this.textBox1.AppendText(message.MyTime.ToString() + " " + message.ToString() + "\r\n");
                }
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            /// bugfix: Before using hasBeenActivated, runs would get repeated if you switched to a different
            /// window and then switched back to the run window
            if (!hasBeenActivated)
            {
                hasBeenActivated = true;
                startRun();
            }


        }


        public void updateTitleBar(bool calibrationShot)
        {
            Action updateBar = () =>
            {
                if (!calibrationShot)
                {
                    string title;
                    if (this.isBackgroundRunform)
                        title = "Background loop running...";
                    else
                    {
                        title = "Running iteration ";
                        switch (runType)
                        {
                            case RunType.Run_Iteration_Zero:
                                title += "0.";
                                break;
                            case RunType.Run_Current_Iteration:
                                title += runningSequence.ListIterationNumber.ToString() + ".";
                                break;
                            case RunType.Run_Full_List:
                                title += runningSequence.ListIterationNumber.ToString() + "/" + (runningSequence.Lists.iterationsCount() - 1) + ".";
                                break;
                            case RunType.Run_Continue_List:
                                title += runningSequence.ListIterationNumber.ToString() + "/" + (runningSequence.Lists.iterationsCount() - 1) + ".";
                                break;
                            case RunType.Run_Random_Order_List:
                                title += runningSequence.ListIterationNumber.ToString() + "/" + (runningSequence.Lists.iterationsCount() - 1) + " (random run #" + random_order_run_iteration_number + ").";
                                break;
                        }

                        if (runRepeat)
                            title += " Repeat #" + repeatCount;
                    }
                    this.Text = title;
                }
                else
                    this.Text = "Running calibration shot.";
            };

            BeginInvoke(updateBar);
        }

        private void startRun()
        {
            // start run! woo hoo!
            // do it async so as not to block the UI thread.

            if (!runningSequence.Lists.ListLocked)
            {
                if (runningSequence != Storage.sequenceData)
                {
                    addMessageLogText(this, new MessageEvent("Cannot lock the lists of a background-running sequence or a calibration shot. Aborting."));
                    setStatus(RunFormStatus.FinishedRun);
                    return;
                }

                addMessageLogText(this, new MessageEvent("Lists not locked, attempting to lock them..."));

                WordGenerator.MainClientForm.instance.variablesEditor.tryLockLists();

                if (!runningSequence.Lists.ListLocked)
                {
                    addMessageLogText(this, new MessageEvent("Unable to lock lists. Aborting run. See the Variables tab."));

                    setStatus(RunFormStatus.FinishedRun);
                    return;
                }
                addMessageLogText(this, new MessageEvent("Lists locked successfully."));
            }

            switch (runType)
            {
                case RunType.Run_Iteration_Zero:
                    Func<int, SequenceData, bool> runZero = do_run;
                    runZero.BeginInvoke(0, runningSequence, null, null);
                    break;
                case RunType.Run_Current_Iteration:
                    Func<SequenceData, bool> runCurrent = do_run;
                    runCurrent.BeginInvoke(runningSequence, null, null);
                    break;
                case RunType.Run_Full_List:
                    Func<bool> runList = do_list_run;
                    runList.BeginInvoke(null, null);
                    break;
                case RunType.Run_Continue_List:
                    Func<bool> runContinueList = do_continue_list_run;
                    runContinueList.BeginInvoke(null, null);
                    break;
                case RunType.Run_Random_Order_List:
                    Func<bool> runRandomList = do_random_order_list_run;
                    runRandomList.BeginInvoke(null, null);
                    break;
            }

        }

        public bool do_continue_list_run()
        {
            addMessageLogText(this, new MessageEvent("Starting continue list run. " + runningSequence.Lists.iterationsCount() + " total iterations. Starting at iteration #" + runningSequence.ListIterationNumber));

            int i = runningSequence.ListIterationNumber;

            bool previousRunSuccessful = calibrationShot(i);
            if (!previousRunSuccessful)
            {
                addMessageLogText(this, new MessageEvent("Aborting list after initial shot."));
                return false;
            }


            for (; i < runningSequence.Lists.iterationsCount(); i++)
            {

                addMessageLogText(this, new MessageEvent("Iteration #" + i));
                previousRunSuccessful = do_run(i, runningSequence);
                if (!previousRunSuccessful)
                {
                    addMessageLogText(this, new MessageEvent("Aborting list run at iteration #" + i));
                    return false;
                }
                if (i != 0)
                {
                    previousRunSuccessful = calibrationShot(i);
                    if (!previousRunSuccessful)
                    {
                        addMessageLogText(this, new MessageEvent("Aborting list after calibration shot, at iteration #" + i));
                        return false;
                    }
                }
            }
            addMessageLogText(this, new MessageEvent("Continue list run successful."));
            return true;
        }

        private bool calibrationShot(int i)
        {
            if (runningSequence.CalibrationShotsInfo.calibrationShotRequiredOnThisRun(i,
                runningSequence.Lists.iterationsCount()))
            {
                addMessageLogText(this, new MessageEvent("Taking a calibration shot."));
                bool temp = do_run(0, runningSequence.calibrationShotsInfo.CalibrationShotSequence, true);
                if (!temp)
                {
                    addMessageLogText(this, new MessageEvent("Calibration shot failed. Aborting list run."));
                    this.ErrorDetected = true;
                }
                return temp;
            }
            else
                return true;
        }

        private int random_order_run_iteration_number;

        public bool do_random_order_list_run()
        {
            addMessageLogText(this, new MessageEvent("Starting random-order list run, " + runningSequence.Lists.iterationsCount() + " iterations."));
            List<int> iterationsRemaining = new List<int>();
            for (int i = 0; i < runningSequence.Lists.iterationsCount(); i++)
            {
                iterationsRemaining.Add(i);
            }

            random_order_run_iteration_number = 0;

            bool previousRunSuccessful = calibrationShot(random_order_run_iteration_number);
            if (!previousRunSuccessful)
            {
                addMessageLogText(this, new MessageEvent("Aborting list after initial calibration shot."));
                return false;
            }

            while (iterationsRemaining.Count != 0)
            {
                Random rand = new Random();
                int selectedIterationIndex = rand.Next(iterationsRemaining.Count);
                int selectedIteration = iterationsRemaining[selectedIterationIndex];

                addMessageLogText(this, new MessageEvent("Iteration # " + selectedIteration + " (randomly selected)."));
                previousRunSuccessful = do_run(selectedIteration, runningSequence);
                if (!previousRunSuccessful)
                {
                    addMessageLogText(this, new MessageEvent("Aborting randomized list run after " + random_order_run_iteration_number + " randomly selected iterations."));
                    return false;
                }
                random_order_run_iteration_number++;

                previousRunSuccessful = calibrationShot(random_order_run_iteration_number);
                if (!previousRunSuccessful)
                {
                    addMessageLogText(this, new MessageEvent("Aborting list after calibration shot."));
                    return false;
                }

                iterationsRemaining.Remove(selectedIteration);
            }
            addMessageLogText(this, new MessageEvent("Randomized list run successful."));
            return true;
        }

        public bool do_list_run()
        {
            addMessageLogText(this, new MessageEvent("Starting list run, " + runningSequence.Lists.iterationsCount() + " iterations."));

            int i = 0;
            bool previousRunSuccessful = calibrationShot(i);
            if (!previousRunSuccessful)
            {
                addMessageLogText(this, new MessageEvent("Aborting list after initial calibration shot."));
                return false;
            }

            for (; i < runningSequence.Lists.iterationsCount(); i++)
            {
                addMessageLogText(this, new MessageEvent("Iteration #" + i));

                previousRunSuccessful = do_run(i, runningSequence);
                if (!previousRunSuccessful)
                {
                    addMessageLogText(this, new MessageEvent("Aborting list run at iteration #" + i));
                    return false;
                }

                if (i != 0)
                {
                    previousRunSuccessful = calibrationShot(i);
                    if (!previousRunSuccessful)
                    {
                        addMessageLogText(this, new MessageEvent("Aborting list after calibration shot, at iteration #" + i));
                        return false;
                    }
                }
            }
            addMessageLogText(this, new MessageEvent("List run successful."));
            return true;
        }

        public bool do_run(SequenceData sequence)
        {
            return do_run(runningSequence.ListIterationNumber, sequence);
        }

        public bool do_run(int iterationNumber, SequenceData sequence)
        {
            return do_run(iterationNumber, sequence, false);
        }

        private UInt32 clockID;

        public bool do_run(int iterationNumber, SequenceData sequence, bool calibrationShot)
        {
            this.runningThread = Thread.CurrentThread;
            bool keepGoing = true;
            while (keepGoing)
            {
                MainClientForm.instance.CurrentlyOutputtingTimestep = null;

                setStatus(RunFormStatus.StartingRun);

                lic_chk();

                if (RunForm.backgroundIsRunning() && !this.isBackgroundRunform)
                {
                    addMessageLogText(this, new MessageEvent("A background run is still running. Waiting for it to terminate..."));
                    RunForm.abortAtEndOfNextBackgroundRun();
                    setStatus(RunFormStatus.ClosableOnly);
                    while (RunForm.backgroundIsRunning())
                    {
                        Thread.Sleep(50);
                    }

                    if (this.IsDisposed)
                    {
                        addMessageLogText(this, new MessageEvent("Foreground run form was closed before background run terminated. Aborting foreground run."));
                        return false;
                    }


                    setStatus(RunFormStatus.StartingRun);

                }

                addMessageLogText(this, new MessageEvent("Starting Run."));



                updateTitleBar(calibrationShot);

                // Begin section of undocumented Paris code that Aviv doesn't understand.
                bool wrongSavePath = false;
                try
                {
                    if (Storage.settingsData.SavePath != "")
                        System.IO.Directory.GetFiles(Storage.settingsData.SavePath);

                }
                catch
                {
                    wrongSavePath = true;
                }

                if (wrongSavePath)
                {
                    addMessageLogText(this, new MessageEvent("Unable to locate save path. Aborting run. See the SavePath setting (under Advanced->Settings Explorer)."));

                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }
                // End section of undocumented Paris code that Aviv doesn't understand

                if (!sequence.Lists.ListLocked)
                {
                    if (calibrationShot)
                    {
                        addMessageLogText(this, new MessageEvent("Calibration shot error -- Lists in the calibration shot are not locked. They must be locked manually. Please open your calibration sequence file, lock the lists, save your calibration sequence, and then re-import the calibration shot in this sequence."));
                        addMessageLogText(this, new MessageEvent("Skipping calibration shot and aborting run as a result of previous error."));
                        ErrorDetected = true;
                        setStatus(RunFormStatus.FinishedRun);
                        return false;
                    }

                    addMessageLogText(this, new MessageEvent("Lists not locked, attempting to lock them..."));

                    WordGenerator.MainClientForm.instance.variablesEditor.tryLockLists();

                    if (!sequence.Lists.ListLocked)
                    {
                        addMessageLogText(this, new MessageEvent("Unable to lock lists. Aborting run. See the Variables tab."));
                        ErrorDetected = true;

                        setStatus(RunFormStatus.FinishedRun);
                        return false;
                    }
                    addMessageLogText(this, new MessageEvent("Lists locked successfully."));
                }



                sequence.ListIterationNumber = iterationNumber;

                string listBoundVariableValues = "";

                foreach (Variable var in sequence.Variables)
                {
                    if (Storage.settingsData.PermanentVariables.ContainsKey(var.VariableName))
                    {
                        var.PermanentVariable = true;
                        var.PermanentValue = Storage.settingsData.PermanentVariables[var.VariableName];
                    }
                    else
                    {
                        var.PermanentVariable = false;
                    }
                }

                foreach (Variable var in sequence.Variables)
                {

                    if (var.ListDriven && !var.PermanentVariable)
                    {
                        if (listBoundVariableValues == "")
                        {
                            listBoundVariableValues = "List bound variable values: ";
                        }
                        listBoundVariableValues += var.VariableName + " = " + var.VariableValue.ToString() + ", ";
                    }
                }

                if (listBoundVariableValues != "")
                {
                    addMessageLogText(this, new MessageEvent(listBoundVariableValues));
                }


                foreach (Variable var in sequence.Variables)
                {
                    if (var.DerivedVariable)
                    {
                        if (var.parseVariableFormula(sequence.Variables) != null)
                        {
                            addMessageLogText(this, new MessageEvent("Warning! Derived variable " + var.ToString() + " has an an error. Will default to 0 for this run."));
                            ErrorDetected = true;
                        }
                    }
                }
                if (!calibrationShot)
                {
                    foreach (Variable var in sequence.Variables)
                    {
                        if (var.VariableName == "SeqMode")
                        {
                            addMessageLogText(this, new MessageEvent("Detected a variable with special name SeqMode. Nearest integer value " + (int)var.VariableValue + "."));
                            int i = (int)var.VariableValue;
                            if (i >= 0 && i < runningSequence.SequenceModes.Count)
                            {
                                SequenceMode mode = runningSequence.SequenceModes[i];
                                if (runningSequence == Storage.sequenceData)
                                {
                                    addMessageLogText(this, new MessageEvent("Settings sequence to sequence mode " + mode.ModeName + "."));
                                    WordGenerator.MainClientForm.instance.sequencePage.setMode(mode);
                                }
                                else
                                {
                                    addMessageLogText(this, new MessageEvent("Currently running sequence is either a calibration shot or background running sequence. Cannot change the sequence mode of a background sequence. Skipping mode change."));
                                }
                            }
                            else
                            {
                                addMessageLogText(this, new MessageEvent("Warning! Invalid sequence mode index. Ignoring the SeqMode variable."));
                                ErrorDetected = true;
                            }
                        }
                    }
                }


                if (variablePreviewForm != null)
                {
                    addMessageLogText(this, new MessageEvent("Updating variables according to variable preview window..."));
                    int nChanged = variablePreviewForm.refresh(sequence);
                    addMessageLogText(this, new MessageEvent("... " + nChanged + " variable values changed."));
                }


                // Create timestep "loop copies" if there are timestep loops in use
                bool useLoops = false;
                foreach (TimestepGroup tsg in sequence.TimestepGroups)
                {
                    if (tsg.LoopTimestepGroup && sequence.TimestepGroupIsLoopable(tsg) && tsg.LoopCountInt > 1)
                    {
                        useLoops = true;
                    }
                }
                if (useLoops)
                {
                    addMessageLogText(this, new MessageEvent("This sequence makes use of looping timestep groups. Creating temporary loop copies..."));
                    sequence.createLoopCopies();
                    addMessageLogText(this, new MessageEvent("...done"));
                }


                List<string> missingServers = Storage.settingsData.unconnectedRequiredServers();

                if (missingServers.Count != 0)
                {

                    string missingServerList = ServerManager.convertListOfServersToOneString(missingServers);

                    addMessageLogText(this, new MessageEvent("Unable to start run. The following required servers are not connected: " + missingServerList + "."));
                    ErrorDetected = true;
                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }


                List<LogicalChannel> overriddenDigitals = new List<LogicalChannel>();
                List<LogicalChannel> overriddenAnalogs = new List<LogicalChannel>();

                foreach (LogicalChannel lc in Storage.settingsData.logicalChannelManager.Digitals.Values)
                {
                    if (lc.overridden)
                        overriddenDigitals.Add(lc);
                }

                foreach (LogicalChannel lc in Storage.settingsData.logicalChannelManager.Analogs.Values)
                {
                    if (lc.overridden)
                        overriddenAnalogs.Add(lc);
                }

                if (overriddenDigitals.Count != 0)
                {
                    string list = "";
                    foreach (LogicalChannel lc in overriddenDigitals)
                    {
                        string actingName;
                        if (lc.Name != "" & lc.Name != null)
                        {
                            actingName = lc.Name;
                        }
                        else
                        {
                            actingName = "[Unnamed]";
                        }
                        list += actingName + ", ";
                    }
                    list = list.Remove(list.Length - 2);
                    list += ".";
                    addMessageLogText(this, new MessageEvent("Reminder. The following " + overriddenDigitals.Count + " digital channel(s) are being overridden: " + list));
                }

                if (overriddenAnalogs.Count != 0)
                {
                    string list = "";
                    foreach (LogicalChannel lc in overriddenAnalogs)
                    {
                        string actingName;
                        if (lc.Name != "" & lc.Name != null)
                        {
                            actingName = lc.Name;
                        }
                        else
                        {
                            actingName = "[Unnamed]";
                        }

                        list += actingName + ", ";
                    }
                    list = list.Remove(list.Length - 2);
                    list += ".";
                    addMessageLogText(this, new MessageEvent("Reminder. The following " + overriddenAnalogs.Count + " analog channel(s) are being overridden: " + list));
                }


                runStartTime = DateTime.Now;

                #region Sending camera instructions
                if (Storage.settingsData.UseCameras)
                {

                    byte[] msg;// = Encoding.ASCII.GetBytes(get_fileStamp(sequence));
                    string shot_name = NamingFunctions.get_fileStamp(sequence, Storage.settingsData, runStartTime);
                    string sequenceTime = sequence.SequenceDuration.ToString();
                    string FCamera;
                    string UCamera;

                    foreach (Socket theSocket in CameraPCsSocketList)
                    {
                        try
                        {
                            int index = CameraPCsSocketList.IndexOf(theSocket);
                            FCamera = connectedPCs[index].useFWCamera.ToString();
                            UCamera = connectedPCs[index].useUSBCamera.ToString();
                            msg = Encoding.ASCII.GetBytes(shot_name + "@" + sequenceTime + "@" + FCamera + "@" + UCamera + "@" + isCameraSaving.ToString() + "@\0");
                            theSocket.Send(msg, 0, msg.Length, SocketFlags.None);
                        }
                        catch { }
                    }
                }
                #endregion

                ServerManager.ServerActionStatus actionStatus;

                // send start timestamp
                addMessageLogText(this, new MessageEvent("Sending run start timestamp."));
                actionStatus = Storage.settingsData.serverManager.setNextRunTimestampOnConnectedServers(runStartTime, addMessageLogText);
                if (actionStatus != ServerManager.ServerActionStatus.Success)
                {
                    addMessageLogText(this, new MessageEvent("Unable to set start timestamp. " + actionStatus.ToString()));
                    ErrorDetected = true;
                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }

                // Code modified by Jere starts here
                // additional channels are added by GS and DC March 2022
                string[] ports = SerialPort.GetPortNames();
                foreach (string port in ports)
                {
                    addMessageLogText(this, new MessageEvent("The following COM port was found: " + port));
                }

                string COMportStr = "3";
                string COMportStr2 = "4";
                string COMportStr3 = "5";
                string COMportStr4 = "3";
                string COMportStr5 = "3";
                string COMportStr6 = "3";
                string COMportStr7 = "3";
                string COMportStr8 = "3"; // the value will be reset by the Cicero, since 3 is the COM port of the desktop, set these to be 3 to aviod overlapping with actual devices

                foreach (Variable var in sequence.Variables)
                {
                    if (var.VariableName == "RSport1")
                    {
                        int COMportInt = (int)var.VariableValue;
                        COMportStr = COMportInt.ToString();
                        addMessageLogText(this, new MessageEvent("Detected a variable with special name RSport1 with value " + COMportStr.ToString() + "."));
                    } else if (var.VariableName == "RSport2")
                    {
                        int COMportInt2 = (int)var.VariableValue;
                        COMportStr2 = COMportInt2.ToString();
                        addMessageLogText(this, new MessageEvent("Detected a variable with special name RSport2 with value " + COMportStr2.ToString() + "."));
                    }
                    else if (var.VariableName == "RSport3")
                    {
                        int COMportInt3 = (int)var.VariableValue;
                        COMportStr3 = COMportInt3.ToString();
                        addMessageLogText(this, new MessageEvent("Detected a variable with special name RSport3 with value " + COMportStr3.ToString() + "."));
                    }
                    else if (var.VariableName == "RSport4")
                    {
                        int COMportInt4 = (int)var.VariableValue;
                        COMportStr4 = COMportInt4.ToString();
                        addMessageLogText(this, new MessageEvent("Detected a variable with special name RSport4 with value " + COMportStr4.ToString() + "."));
                    }
                    else if (var.VariableName == "RSport5")
                    {
                        int COMportInt5 = (int)var.VariableValue;
                        COMportStr5 = COMportInt5.ToString();
                        addMessageLogText(this, new MessageEvent("Detected a variable with special name RSport5 with value " + COMportStr5.ToString() + "."));
                    }
                    else if (var.VariableName == "RSport6")
                    {
                        int COMportInt6 = (int)var.VariableValue;
                        COMportStr6 = COMportInt6.ToString();
                        addMessageLogText(this, new MessageEvent("Detected a variable with special name RSport6 with value " + COMportStr6.ToString() + "."));
                    }
                    else if (var.VariableName == "RSport7")
                    {
                        int COMportInt7 = (int)var.VariableValue;
                        COMportStr7 = COMportInt7.ToString();
                        addMessageLogText(this, new MessageEvent("Detected a variable with special name RSport7 with value " + COMportStr7.ToString() + "."));
                    }
                    else if (var.VariableName == "RSport8")
                    {
                        int COMportInt8 = (int)var.VariableValue;
                        COMportStr8 = COMportInt8.ToString();
                        addMessageLogText(this, new MessageEvent("Detected a variable with special name RSport8 with value " + COMportStr8.ToString() + "."));
                    }
                }

                // Sets up a new serial port connection if one does not exist yet
                if (!serialConnected)
                {
                    _serialPort = new SerialPort("COM" + COMportStr, 115200); // Start communication with SRS
                    _serialPort.Parity = Parity.None;
                    _serialPort.StopBits = StopBits.One;
                    _serialPort.DataBits = 8;
                    _serialPort.DtrEnable = true;
                    _serialPort.RtsEnable = true;
                    _serialPort.Handshake = Handshake.None;
                    try
                    {
                        _serialPort.Open();
                        if (_serialPort.IsOpen)
                        {
                            serialConnected = true;
                            addMessageLogText(this, new MessageEvent("Connection to device on port COM" + COMportStr + " established."));
                        }
                        else
                        {
                            addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr + "."));
                        }

                    }
                    catch (Exception ex)
                    {
                        addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr + ". Use variable named RSport1 to change this value."));
                    }
                }

                // Serial 2
                if (!serial2Connected)
                {
                    _serialPort2 = new SerialPort("COM" + COMportStr2, 115200); // Start communication with SRS
                    _serialPort2.Parity = Parity.None;
                    _serialPort2.StopBits = StopBits.One;
                    _serialPort2.DataBits = 8;
                    _serialPort2.DtrEnable = true;
                    _serialPort2.RtsEnable = true;
                    _serialPort2.Handshake = Handshake.None;
                    try
                    {
                        _serialPort2.Open();
                        if (_serialPort2.IsOpen)
                        {
                            serial2Connected = true;
                            addMessageLogText(this, new MessageEvent("Connection to device on port COM" + COMportStr2 + " established."));
                        }
                        else
                        {
                            addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr2 + "."));
                        }

                    }
                    catch (Exception ex)
                    {
                        addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr2 + ". Use variable named RSport2 to change this value."));
                    }
                }

                // Serial 3
                if (!serial3Connected)
                {
                    _serialPort3 = new SerialPort("COM" + COMportStr3, 9600); // Start communication with SRS PID, should be 9600 BAUD, 
                    _serialPort3.Parity = Parity.None;
                    _serialPort3.StopBits = StopBits.One;
                    _serialPort3.DataBits = 8;
                    _serialPort3.DtrEnable = true; //set to false for PID?

                    _serialPort3.RtsEnable = true;
                    _serialPort3.Handshake = Handshake.None; //Use RequestToSendXOnXOff to emulate RTS/CTS, following microsoft docs.
                    try
                    {
                        _serialPort3.Open();
                        if (_serialPort3.IsOpen)
                        {
                            serial3Connected = true;
                            addMessageLogText(this, new MessageEvent("Connection to device on port COM" + COMportStr3 + " established."));
                        }
                        else
                        {
                            addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr3 + "."));
                        }

                    }
                    catch (Exception ex)
                    {
                        addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr3 + ". Use variable named RSport3 to change this value."));
                    }
                }

                // Serial 4
                if (!serial4Connected)
                {
                    _serialPort4 = new SerialPort("COM" + COMportStr, 115200); // Start communication with SRS
                    _serialPort4.Parity = Parity.None;
                    _serialPort4.StopBits = StopBits.One;
                    _serialPort4.DataBits = 8;
                    _serialPort4.DtrEnable = true;
                    _serialPort4.RtsEnable = true;
                    _serialPort4.Handshake = Handshake.None;
                    try
                    {
                        _serialPort4.Open();
                        if (_serialPort4.IsOpen)
                        {
                            serial4Connected = true;
                            addMessageLogText(this, new MessageEvent("Connection to device on port COM" + COMportStr4 + " established."));
                        }
                        else
                        {
                            addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr4 + "."));
                        }

                    }
                    catch (Exception ex)
                    {
                        addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr4 + ". Use variable named RSport4 to change this value."));
                    }
                }

                // Serial 5
                if (!serial5Connected)
                {
                    _serialPort5 = new SerialPort("COM" + COMportStr5, 115200); // Start communication with SRS
                    _serialPort5.Parity = Parity.None;
                    _serialPort5.StopBits = StopBits.One;
                    _serialPort5.DataBits = 8;
                    _serialPort5.DtrEnable = true;
                    _serialPort5.RtsEnable = true;
                    _serialPort5.Handshake = Handshake.None;
                    try
                    {
                        _serialPort5.Open();
                        if (_serialPort5.IsOpen)
                        {
                            serial5Connected = true;
                            addMessageLogText(this, new MessageEvent("Connection to device on port COM" + COMportStr5 + " established."));
                        }
                        else
                        {
                            addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr5 + "."));
                        }

                    }
                    catch (Exception ex)
                    {
                        addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr5 + ". Use variable named RSport5 to change this value."));
                    }
                }

                // Serial 6
                if (!serial6Connected)
                {
                    _serialPort6 = new SerialPort("COM" + COMportStr6, 115200); // Start communication with SRS
                    _serialPort6.Parity = Parity.None;
                    _serialPort6.StopBits = StopBits.One;
                    _serialPort6.DataBits = 8;
                    _serialPort6.DtrEnable = true;
                    _serialPort6.RtsEnable = true;
                    _serialPort6.Handshake = Handshake.None;
                    try
                    {
                        _serialPort6.Open();
                        if (_serialPort6.IsOpen)
                        {
                            serial6Connected = true;
                            addMessageLogText(this, new MessageEvent("Connection to device on port COM" + COMportStr6 + " established."));
                        }
                        else
                        {
                            addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr6 + "."));
                        }

                    }
                    catch (Exception ex)
                    {
                        addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr6 + ". Use variable named RSport6 to change this value."));
                    }
                }

                // Serial 7
                if (!serial7Connected)
                {
                    _serialPort7 = new SerialPort("COM" + COMportStr7, 115200); // Start communication with SRS
                    _serialPort7.Parity = Parity.None;
                    _serialPort7.StopBits = StopBits.One;
                    _serialPort7.DataBits = 8;
                    _serialPort7.DtrEnable = true;
                    _serialPort7.RtsEnable = true;
                    _serialPort7.Handshake = Handshake.None;
                    try
                    {
                        _serialPort7.Open();
                        if (_serialPort7.IsOpen)
                        {
                            serial7Connected = true;
                            addMessageLogText(this, new MessageEvent("Connection to device on port COM" + COMportStr7 + " established."));
                        }
                        else
                        {
                            addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr7 + "."));
                        }

                    }
                    catch (Exception ex)
                    {
                        addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr7 + ". Use variable named RSport7 to change this value."));
                    }
                }

                // Serial 8
                if (!serial8Connected)
                {
                    _serialPort8 = new SerialPort("COM" + COMportStr8, 115200); // Start communication with SRS
                    _serialPort8.Parity = Parity.None;
                    _serialPort8.StopBits = StopBits.One;
                    _serialPort8.DataBits = 8;
                    _serialPort8.DtrEnable = true;
                    _serialPort8.RtsEnable = true;
                    _serialPort8.Handshake = Handshake.None;
                    try
                    {
                        _serialPort8.Open();
                        if (_serialPort8.IsOpen)
                        {
                            serial8Connected = true;
                            addMessageLogText(this, new MessageEvent("Connection to device on port COM" + COMportStr8 + " established."));
                        }
                        else
                        {
                            addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr8 + "."));
                        }

                    }
                    catch (Exception ex)
                    {
                        addMessageLogText(this, new MessageEvent("Error opening the serial port COM" + COMportStr8 + ". Use variable named RSport8 to change this value."));
                    }
                }

                float DMDfreq = -1; // If -1, the DMD will ignore this setting
                int UpdateDMD = 0; // If not 1, the DMD will not update the sequence list

                foreach (Variable var in sequence.Variables)
                {
                    if (var.VariableName == "DMDfreq")
                    {
                        DMDfreq = (float)var.VariableValue;
                        addMessageLogText(this, new MessageEvent("Detected a variable with special name DMDfreq."));
                        if (DMDfreq < 0.1)
                        {
                            addMessageLogText(this, new MessageEvent("Value of DMDfreq was out of valid range 0.1-22,727 Hz. Setting DMDfreq = 0.1 Hz."));
                            DMDfreq = (float)0.1;
                        }
                        else if (DMDfreq > 22727)
                        {
                            addMessageLogText(this, new MessageEvent("Value of DMDfreq was out of valid range 0.1-22,727 Hz. Setting DMDfreq = 22,727 Hz."));
                            DMDfreq = (float)22727;
                        }
                        addMessageLogText(this, new MessageEvent("Setting DMD refresh rate to " + (float)DMDfreq + " Hz if variable DMDupdt is set to 1."));
                    }
                }

                foreach (Variable var in sequence.Variables)
                {
                    if (var.VariableName == "DMDupdt")
                    {
                        if ((int)var.VariableValue == 1)
                        {
                            addMessageLogText(this, new MessageEvent("Detected a variable with special name DMDupdt with value 1. Adding all uploaded sequences to DMD queue in order."));
                            UpdateDMD = 1;
                        }
                        else
                        {
                            addMessageLogText(this, new MessageEvent("Detected a variable with special name DMDupdt with value " + (int)var.VariableValue + ". DMD sequence list will not be updated."));
                        }
                    }
                }
                string DMDfreqStr = DMDfreq.ToString("####0.##");
                DMDfreqStr = DMDfreqStr.Replace(",", ".");
                string DMDFilePath = @"\\storage.yale.edu\home\navon-627019-FASPHY\Li Lab\DMD\Runtime\DMDruntime.txt";
                string[] txt2write = { UpdateDMD.ToString(), DMDfreqStr, "This file is written automatically by Cicero - do not modify by hand!" };
                // Write array of strings to a file using WriteAllLines.  
                // If the file does not exists, it will create a new file.  
                // This method automatically opens the file, (over)writes to it, and closes file  
                

                // Code modified by Jere ends here

                // send settings data.
                addMessageLogText(this, new MessageEvent("Sending settings data."));
                actionStatus = Storage.settingsData.serverManager.setSettingsOnConnectedServers(Storage.settingsData, addMessageLogText);
                if (actionStatus != ServerManager.ServerActionStatus.Success)
                {
                    addMessageLogText(this, new MessageEvent("Unable to send settings data. " + actionStatus.ToString()));
                    ErrorDetected = true;
                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }

                // send sequence data.
                addMessageLogText(this, new MessageEvent("Sending sequence data."));
                actionStatus = Storage.settingsData.serverManager.setSequenceOnConnectedServers(sequence, addMessageLogText);
                if (actionStatus != ServerManager.ServerActionStatus.Success)
                {
                    addMessageLogText(this, new MessageEvent("Unable to send sequence data. " + actionStatus.ToString()));
                    ErrorDetected = true;
                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }

                // generate buffers. 
                addMessageLogText(this, new MessageEvent("Generating buffers."));
                actionStatus = Storage.settingsData.serverManager.generateBuffersOnConnectedServers(iterationNumber, addMessageLogText);
                if (actionStatus != ServerManager.ServerActionStatus.Success)
                {
                    addMessageLogText(this, new MessageEvent("Unable to generate buffers. " + actionStatus.ToString()));
                    ErrorDetected = true;
                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }


                // arm tasks.


                Random rnd = new Random();
                clockID = (uint)rnd.Next();

                if (softwareClockProvider != null || networkClockProvider != null)
                {
                    addMessageLogText(this, new MessageEvent("A software clock provider already exists, unexpectedly. Aborting."));
                    return false;
                }

                if (!Storage.settingsData.AlwaysUseNetworkClock)
                {
                    softwareClockProvider = new ComputerClockSoftwareClockProvider(10);
                    softwareClockProvider.addSubscriber(this, 41, 0);
                    softwareClockProvider.ArmClockProvider();
                }

                networkClockProvider = new NetworkClockProvider(clockID);
                networkClockProvider.addSubscriber(this, 41, 1);
                networkClockProvider.ArmClockProvider();

                currentSoftwareclockPriority = 0;


                addMessageLogText(this, new MessageEvent("Arming tasks."));
                actionStatus = Storage.settingsData.serverManager.armTasksOnConnectedServers(clockID, addMessageLogText);
                if (actionStatus != ServerManager.ServerActionStatus.Success)
                {
                    addMessageLogText(this, new MessageEvent("Unable to arm tasks. " + actionStatus.ToString()));
                    ErrorDetected = true;
                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }

                // generate triggers

                addMessageLogText(this, new MessageEvent("Generating triggers."));
                actionStatus = Storage.settingsData.serverManager.generateTriggersOnConnectedServers(addMessageLogText);
                if (actionStatus != ServerManager.ServerActionStatus.Success)
                {
                    addMessageLogText(this, new MessageEvent("Unable to generate triggers. " + actionStatus.ToString()));
                    ErrorDetected = true;
                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }

                setStatus(RunFormStatus.Running);


                // async call to progress bar initialization

                Action<double> initProgressBarAction = initializeProgressBar;
                Invoke(initProgressBarAction, new object[] { sequence.SequenceDuration });


                double duration = sequence.SequenceDuration;
                addMessageLogText(this, new MessageEvent("Sequence duration " + duration + " s. Running."));


                // start software clock
                if (softwareClockProvider != null)
                    softwareClockProvider.StartClockProvider();
                networkClockProvider.StartClockProvider();

                // The following part of the code has been modified by Jere (further modified by GS DC March 2022)

                bool RsOutput1 = true;
                bool RsOutput2 = true;
                bool RsOutput3 = true;
                bool RsOutput4 = true;
                bool RsOutput5 = true;
                bool RsOutput6 = true;
                bool RsOutput7 = true;
                bool RsOutput8 = true;

                int startIndex = -1;
                int startIndex2 = -1;
                int startIndex3 = -1;
                int startIndex4 = -1;
                int startIndex5 = -1;
                int startIndex6 = -1;
                int startIndex7 = -1;
                int startIndex8 = -1;

                int rs232ID = 0;  // Logical channel ID for first RS232
                int rs232ID2 = 1; // Logical channel ID for second RS232
                int rs232ID3 = 2; // Logical channel ID for third RS232
                int rs232ID4 = 3; // Logical channel ID for fourth RS232
                int rs232ID5 = 4; // Logical channel ID for fifth RS232
                int rs232ID6 = 5; // Logical channel ID for sixth RS232
                int rs232ID7 = 6; // Logical channel ID for seventh RS232
                int rs232ID8 = 7; // Logical channel ID for eighth RS232

                double nextRS232cmd = -1;
                double nextRS232cmd2 = -1;
                double nextRS232cmd3 = -1;
                double nextRS232cmd4 = -1;
                double nextRS232cmd5 = -1;
                double nextRS232cmd6 = -1;
                double nextRS232cmd7 = -1;
                double nextRS232cmd8 = -1;

                // The following part of the code finds the next enabled rs command in the sequence for device 1
                int rs232EnabledID = sequence.findNextRS232ChannelEnabledTimestep(startIndex, rs232ID); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                if (sequence.TimeSteps.Count > rs232EnabledID) // An enabled step exists after the selected index
                {
                    nextRS232cmd = sequence.timeAtTimestep(rs232EnabledID); // time at start of timestep
                    startIndex = rs232EnabledID; // or this -1? This is the index at the start of the searc for the next command
                }

                if (sequence.TimeSteps.Count > rs232EnabledID) // An enabled step exists after the selected index
                {
                    channelData = sequence.TimeSteps[rs232EnabledID].rs232Group.getChannelData(rs232ID); // Fetch the timestep data

                    if (channelData.DataType == RS232GroupChannelData.RS232DataType.Raw)
                    {
                        // Raw string commands just get added 
                        rs232cmd = channelData.RawString;
                    }
                    else if (channelData.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                    {
                        if (channelData.StringParameterStrings != null)
                        {
                            foreach (StringParameterString srs in channelData.StringParameterStrings)
                            {
                                rs232cmd = srs.ToString();
                            }
                        }
                    }
                }

                // Device 2
                int rs232EnabledID2 = sequence.findNextRS232ChannelEnabledTimestep(startIndex2, rs232ID2); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                if (sequence.TimeSteps.Count > rs232EnabledID2) // An enabled step exists after the selected index
                {
                    nextRS232cmd2 = sequence.timeAtTimestep(rs232EnabledID2); // time at start of timestep
                    startIndex2 = rs232EnabledID2; // or this -1? This is the index at the start of the searc for the next command
                }

                if (sequence.TimeSteps.Count > rs232EnabledID2) // An enabled step exists after the selected index
                {
                    channelData2 = sequence.TimeSteps[rs232EnabledID2].rs232Group.getChannelData(rs232ID2); // Fetch the timestep data

                    if (channelData2.DataType == RS232GroupChannelData.RS232DataType.Raw)
                    {
                        // Raw string commands just get added 
                        rs232cmd2 = channelData2.RawString;
                    }
                    else if (channelData2.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                    {
                        if (channelData2.StringParameterStrings != null)
                        {
                            foreach (StringParameterString srs in channelData2.StringParameterStrings)
                            {
                                rs232cmd2 = srs.ToString();
                            }
                        }
                    }
                }

                // Device 3
                int rs232EnabledID3 = sequence.findNextRS232ChannelEnabledTimestep(startIndex3, rs232ID3); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                if (sequence.TimeSteps.Count > rs232EnabledID3) // An enabled step exists after the selected index
                {
                    nextRS232cmd3 = sequence.timeAtTimestep(rs232EnabledID3); // time at start of timestep
                    startIndex3 = rs232EnabledID3; // or this -1? This is the index at the start of the searc for the next command
                }

                if (sequence.TimeSteps.Count > rs232EnabledID3) // An enabled step exists after the selected index
                {
                    channelData3 = sequence.TimeSteps[rs232EnabledID3].rs232Group.getChannelData(rs232ID3); // Fetch the timestep data

                    if (channelData3.DataType == RS232GroupChannelData.RS232DataType.Raw)
                    {
                        // Raw string commands just get added 
                        rs232cmd3 = channelData3.RawString;
                    }
                    else if (channelData3.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                    {
                        if (channelData3.StringParameterStrings != null)
                        {
                            foreach (StringParameterString srs in channelData3.StringParameterStrings)
                            {
                                rs232cmd3 = srs.ToString();
                            }
                        }
                    }
                }

                // Device 4
                int rs232EnabledID4 = sequence.findNextRS232ChannelEnabledTimestep(startIndex4, rs232ID4); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                if (sequence.TimeSteps.Count > rs232EnabledID4) // An enabled step exists after the selected index
                {
                    nextRS232cmd4 = sequence.timeAtTimestep(rs232EnabledID4); // time at start of timestep
                    startIndex4 = rs232EnabledID4; // or this -1? This is the index at the start of the searc for the next command
                }

                if (sequence.TimeSteps.Count > rs232EnabledID4) // An enabled step exists after the selected index
                {
                    channelData4 = sequence.TimeSteps[rs232EnabledID4].rs232Group.getChannelData(rs232ID4); // Fetch the timestep data

                    if (channelData4.DataType == RS232GroupChannelData.RS232DataType.Raw)
                    {
                        // Raw string commands just get added 
                        rs232cmd4 = channelData4.RawString;
                    }
                    else if (channelData4.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                    {
                        if (channelData4.StringParameterStrings != null)
                        {
                            foreach (StringParameterString srs in channelData4.StringParameterStrings)
                            {
                                rs232cmd4 = srs.ToString();
                            }
                        }
                    }
                }

                // Device 5
                int rs232EnabledID5 = sequence.findNextRS232ChannelEnabledTimestep(startIndex5, rs232ID5); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                if (sequence.TimeSteps.Count > rs232EnabledID5) // An enabled step exists after the selected index
                {
                    nextRS232cmd5 = sequence.timeAtTimestep(rs232EnabledID5); // time at start of timestep
                    startIndex5 = rs232EnabledID5; // or this -1? This is the index at the start of the searc for the next command
                }

                if (sequence.TimeSteps.Count > rs232EnabledID5) // An enabled step exists after the selected index
                {
                    channelData5 = sequence.TimeSteps[rs232EnabledID5].rs232Group.getChannelData(rs232ID5); // Fetch the timestep data

                    if (channelData5.DataType == RS232GroupChannelData.RS232DataType.Raw)
                    {
                        // Raw string commands just get added 
                        rs232cmd5 = channelData5.RawString;
                    }
                    else if (channelData5.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                    {
                        if (channelData5.StringParameterStrings != null)
                        {
                            foreach (StringParameterString srs in channelData5.StringParameterStrings)
                            {
                                rs232cmd5 = srs.ToString();
                            }
                        }
                    }
                }

                // Device 6
                int rs232EnabledID6 = sequence.findNextRS232ChannelEnabledTimestep(startIndex6, rs232ID6); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                if (sequence.TimeSteps.Count > rs232EnabledID6) // An enabled step exists after the selected index
                {
                    nextRS232cmd6 = sequence.timeAtTimestep(rs232EnabledID6); // time at start of timestep
                    startIndex6 = rs232EnabledID6; // or this -1? This is the index at the start of the searc for the next command
                }

                if (sequence.TimeSteps.Count > rs232EnabledID6) // An enabled step exists after the selected index
                {
                    channelData6 = sequence.TimeSteps[rs232EnabledID6].rs232Group.getChannelData(rs232ID6); // Fetch the timestep data

                    if (channelData6.DataType == RS232GroupChannelData.RS232DataType.Raw)
                    {
                        // Raw string commands just get added 
                        rs232cmd6 = channelData6.RawString;
                    }
                    else if (channelData6.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                    {
                        if (channelData6.StringParameterStrings != null)
                        {
                            foreach (StringParameterString srs in channelData6.StringParameterStrings)
                            {
                                rs232cmd6 = srs.ToString();
                            }
                        }
                    }
                }

                // Device 7
                int rs232EnabledID7 = sequence.findNextRS232ChannelEnabledTimestep(startIndex7, rs232ID7); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                if (sequence.TimeSteps.Count > rs232EnabledID7) // An enabled step exists after the selected index
                {
                    nextRS232cmd7 = sequence.timeAtTimestep(rs232EnabledID7); // time at start of timestep
                    startIndex7 = rs232EnabledID7; // or this -1? This is the index at the start of the searc for the next command
                }

                if (sequence.TimeSteps.Count > rs232EnabledID7) // An enabled step exists after the selected index
                {
                    channelData7 = sequence.TimeSteps[rs232EnabledID7].rs232Group.getChannelData(rs232ID7); // Fetch the timestep data

                    if (channelData7.DataType == RS232GroupChannelData.RS232DataType.Raw)
                    {
                        // Raw string commands just get added 
                        rs232cmd7 = channelData7.RawString;
                    }
                    else if (channelData7.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                    {
                        if (channelData7.StringParameterStrings != null)
                        {
                            foreach (StringParameterString srs in channelData7.StringParameterStrings)
                            {
                                rs232cmd7 = srs.ToString();
                            }
                        }
                    }
                }

                // Device 8
                int rs232EnabledID8 = sequence.findNextRS232ChannelEnabledTimestep(startIndex8, rs232ID8); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                if (sequence.TimeSteps.Count > rs232EnabledID8) // An enabled step exists after the selected index
                {
                    nextRS232cmd8 = sequence.timeAtTimestep(rs232EnabledID8); // time at start of timestep
                    startIndex8 = rs232EnabledID8; // or this -1? This is the index at the start of the searc for the next command
                }

                if (sequence.TimeSteps.Count > rs232EnabledID8) // An enabled step exists after the selected index
                {
                    channelData8 = sequence.TimeSteps[rs232EnabledID8].rs232Group.getChannelData(rs232ID8); // Fetch the timestep data

                    if (channelData8.DataType == RS232GroupChannelData.RS232DataType.Raw)
                    {
                        // Raw string commands just get added 
                        rs232cmd8 = channelData8.RawString;
                    }
                    else if (channelData8.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                    {
                        if (channelData8.StringParameterStrings != null)
                        {
                            foreach (StringParameterString srs in channelData8.StringParameterStrings)
                            {
                                rs232cmd8 = srs.ToString();
                            }
                        }
                    }
                }

                System.IO.File.WriteAllLines(DMDFilePath, txt2write);
                addMessageLogText(this, new MessageEvent("DMD output file written to " + DMDFilePath));

                while (true)
                {
                    double currentSeqTime = softwareClockProvider.getElapsedTime();

                    if (currentSoftwareclockPriority == 0)
                    {
                        if (softwareClockProvider != null && (currentSeqTime >= (200 + duration * 1000.0)))
                        {
                            break;
                        }
                        else if (softwareClockProvider != null && (currentSeqTime >= (nextRS232cmd * 1000.0)) && (RsOutput1 == true) && (serialConnected))
                        {
                            addMessageLogText(this, new MessageEvent("Command " + rs232cmd + " was executed at time t = " + currentSeqTime/1000 + " s."));
                            _serialPort.WriteLine(rs232cmd);
                            rs232EnabledID = sequence.findNextRS232ChannelEnabledTimestep(startIndex, rs232ID); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                            if (sequence.TimeSteps.Count > rs232EnabledID) // An enabled step exists after the selected index
                            {
                                channelData = sequence.TimeSteps[rs232EnabledID].rs232Group.getChannelData(rs232ID); // Fetch the timestep data
                                nextRS232cmd = sequence.timeAtTimestep(rs232EnabledID); // time at start of timestep
                                startIndex = rs232EnabledID; // or this -1? This is the index at the start of the searc for the next command

                                if (channelData.DataType == RS232GroupChannelData.RS232DataType.Raw)
                                {
                                    // Raw string commands just get added 
                                    rs232cmd = channelData.RawString;
                                }
                                else if (channelData.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                                {
                                    if (channelData.StringParameterStrings != null)
                                    {
                                        foreach (StringParameterString srs in channelData.StringParameterStrings)
                                        {
                                            rs232cmd = srs.ToString().Replace(",", ".");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                RsOutput1 = false; // Last RS232 command for dev 1 has been executed
                            }
                        }
                        else if (softwareClockProvider != null && (currentSeqTime >= (nextRS232cmd2 * 1000.0)) && (RsOutput2 == true) && (serial2Connected))
                        {
                            addMessageLogText(this, new MessageEvent("Command " + rs232cmd2 + " was executed at time t = " + currentSeqTime/1000 + " s."));
                            _serialPort2.WriteLine(rs232cmd2);
                            rs232EnabledID2 = sequence.findNextRS232ChannelEnabledTimestep(startIndex2, rs232ID2); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                            if (sequence.TimeSteps.Count > rs232EnabledID2) // An enabled step exists after the selected index
                            {
                                channelData2 = sequence.TimeSteps[rs232EnabledID2].rs232Group.getChannelData(rs232ID2); // Fetch the timestep data
                                nextRS232cmd2 = sequence.timeAtTimestep(rs232EnabledID2); // time at start of timestep
                                startIndex2 = rs232EnabledID2; // or this -1? This is the index at the start of the searc for the next command

                                if (channelData2.DataType == RS232GroupChannelData.RS232DataType.Raw)
                                {
                                    // Raw string commands just get added 
                                    rs232cmd2 = channelData2.RawString;
                                }
                                else if (channelData2.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                                {
                                    if (channelData2.StringParameterStrings != null)
                                    {
                                        foreach (StringParameterString srs in channelData2.StringParameterStrings)
                                        {
                                            rs232cmd2 = srs.ToString().Replace(",", ".");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                RsOutput2 = false; // Last RS232 command for dev 2 has been executed
                            }
                        }
                        else if (softwareClockProvider != null && (currentSeqTime >= (nextRS232cmd3 * 1000.0)) && (RsOutput3 == true) && (serial3Connected))
                        {
                            addMessageLogText(this, new MessageEvent("Command " + rs232cmd3 + " was executed at time t = " + currentSeqTime / 1000 + " s."));
                            _serialPort3.WriteLine(rs232cmd3);
                            rs232EnabledID3 = sequence.findNextRS232ChannelEnabledTimestep(startIndex3, rs232ID3); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                            if (sequence.TimeSteps.Count > rs232EnabledID3) // An enabled step exists after the selected index
                            {
                                channelData3 = sequence.TimeSteps[rs232EnabledID3].rs232Group.getChannelData(rs232ID3); // Fetch the timestep data
                                nextRS232cmd3 = sequence.timeAtTimestep(rs232EnabledID3); // time at start of timestep
                                startIndex3 = rs232EnabledID3; // or this -1? This is the index at the start of the searc for the next command

                                if (channelData3.DataType == RS232GroupChannelData.RS232DataType.Raw)
                                {
                                    // Raw string commands just get added 
                                    rs232cmd3 = channelData3.RawString;
                                }
                                else if (channelData3.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                                {
                                    if (channelData3.StringParameterStrings != null)
                                    {
                                        foreach (StringParameterString srs in channelData3.StringParameterStrings)
                                        {
                                            rs232cmd3 = srs.ToString().Replace(",", ".");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                RsOutput3 = false; // Last RS232 command for dev 3 has been executed
                            }
                        }
                        else if (softwareClockProvider != null && (currentSeqTime >= (nextRS232cmd4 * 1000.0)) && (RsOutput4 == true) && (serial4Connected))
                        {
                            addMessageLogText(this, new MessageEvent("Command " + rs232cmd4 + " was executed at time t = " + currentSeqTime / 1000 + " s."));
                            _serialPort4.WriteLine(rs232cmd4);
                            rs232EnabledID4 = sequence.findNextRS232ChannelEnabledTimestep(startIndex4, rs232ID4); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                            if (sequence.TimeSteps.Count > rs232EnabledID4) // An enabled step exists after the selected index
                            {
                                channelData4 = sequence.TimeSteps[rs232EnabledID4].rs232Group.getChannelData(rs232ID4); // Fetch the timestep data
                                nextRS232cmd4 = sequence.timeAtTimestep(rs232EnabledID4); // time at start of timestep
                                startIndex4 = rs232EnabledID4; // or this -1? This is the index at the start of the searc for the next command

                                if (channelData4.DataType == RS232GroupChannelData.RS232DataType.Raw)
                                {
                                    // Raw string commands just get added 
                                    rs232cmd4 = channelData4.RawString;
                                }
                                else if (channelData4.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                                {
                                    if (channelData4.StringParameterStrings != null)
                                    {
                                        foreach (StringParameterString srs in channelData4.StringParameterStrings)
                                        {
                                            rs232cmd4 = srs.ToString().Replace(",", ".");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                RsOutput4 = false; // Last RS232 command for dev 4 has been executed
                            }
                        }
                        else if (softwareClockProvider != null && (currentSeqTime >= (nextRS232cmd5 * 1000.0)) && (RsOutput5 == true) && (serial5Connected))
                        {
                            addMessageLogText(this, new MessageEvent("Command " + rs232cmd5 + " was executed at time t = " + currentSeqTime / 1000 + " s."));
                            _serialPort5.WriteLine(rs232cmd5);
                            rs232EnabledID5 = sequence.findNextRS232ChannelEnabledTimestep(startIndex5, rs232ID5); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                            if (sequence.TimeSteps.Count > rs232EnabledID5) // An enabled step exists after the selected index
                            {
                                channelData5 = sequence.TimeSteps[rs232EnabledID5].rs232Group.getChannelData(rs232ID5); // Fetch the timestep data
                                nextRS232cmd5 = sequence.timeAtTimestep(rs232EnabledID5); // time at start of timestep
                                startIndex5 = rs232EnabledID5; // or this -1? This is the index at the start of the searc for the next command

                                if (channelData5.DataType == RS232GroupChannelData.RS232DataType.Raw)
                                {
                                    // Raw string commands just get added 
                                    rs232cmd5 = channelData5.RawString;
                                }
                                else if (channelData5.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                                {
                                    if (channelData5.StringParameterStrings != null)
                                    {
                                        foreach (StringParameterString srs in channelData5.StringParameterStrings)
                                        {
                                            rs232cmd5 = srs.ToString().Replace(",", ".");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                RsOutput5 = false; // Last RS232 command for dev 5 has been executed
                            }
                        }
                        else if (softwareClockProvider != null && (currentSeqTime >= (nextRS232cmd6 * 1000.0)) && (RsOutput6 == true) && (serial6Connected))
                        {
                            addMessageLogText(this, new MessageEvent("Command " + rs232cmd6 + " was executed at time t = " + currentSeqTime / 1000 + " s."));
                            _serialPort6.WriteLine(rs232cmd6);
                            rs232EnabledID6 = sequence.findNextRS232ChannelEnabledTimestep(startIndex6, rs232ID6); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                            if (sequence.TimeSteps.Count > rs232EnabledID6) // An enabled step exists after the selected index
                            {
                                channelData6 = sequence.TimeSteps[rs232EnabledID6].rs232Group.getChannelData(rs232ID6); // Fetch the timestep data
                                nextRS232cmd6 = sequence.timeAtTimestep(rs232EnabledID6); // time at start of timestep
                                startIndex6 = rs232EnabledID6; // or this -1? This is the index at the start of the searc for the next command

                                if (channelData6.DataType == RS232GroupChannelData.RS232DataType.Raw)
                                {
                                    // Raw string commands just get added 
                                    rs232cmd6 = channelData6.RawString;
                                }
                                else if (channelData6.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                                {
                                    if (channelData6.StringParameterStrings != null)
                                    {
                                        foreach (StringParameterString srs in channelData6.StringParameterStrings)
                                        {
                                            rs232cmd6 = srs.ToString().Replace(",", ".");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                RsOutput6 = false; // Last RS232 command for dev 6 has been executed
                            }
                        }
                        else if (softwareClockProvider != null && (currentSeqTime >= (nextRS232cmd7 * 1000.0)) && (RsOutput7 == true) && (serial7Connected))
                        {
                            addMessageLogText(this, new MessageEvent("Command " + rs232cmd7 + " was executed at time t = " + currentSeqTime / 1000 + " s."));
                            _serialPort7.WriteLine(rs232cmd7);
                            rs232EnabledID7 = sequence.findNextRS232ChannelEnabledTimestep(startIndex7, rs232ID7); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                            if (sequence.TimeSteps.Count > rs232EnabledID7) // An enabled step exists after the selected index
                            {
                                channelData7 = sequence.TimeSteps[rs232EnabledID7].rs232Group.getChannelData(rs232ID7); // Fetch the timestep data
                                nextRS232cmd7 = sequence.timeAtTimestep(rs232EnabledID7); // time at start of timestep
                                startIndex7 = rs232EnabledID7; // or this -1? This is the index at the start of the searc for the next command

                                if (channelData7.DataType == RS232GroupChannelData.RS232DataType.Raw)
                                {
                                    // Raw string commands just get added 
                                    rs232cmd7 = channelData7.RawString;
                                }
                                else if (channelData7.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                                {
                                    if (channelData7.StringParameterStrings != null)
                                    {
                                        foreach (StringParameterString srs in channelData7.StringParameterStrings)
                                        {
                                            rs232cmd7 = srs.ToString().Replace(",", ".");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                RsOutput7 = false; // Last RS232 command for dev 7 has been executed
                            }
                        }
                        else if (softwareClockProvider != null && (currentSeqTime >= (nextRS232cmd8 * 1000.0)) && (RsOutput8 == true) && (serial8Connected))
                        {
                            addMessageLogText(this, new MessageEvent("Command " + rs232cmd8 + " was executed at time t = " + currentSeqTime / 1000 + " s."));
                            _serialPort8.WriteLine(rs232cmd8);
                            rs232EnabledID8 = sequence.findNextRS232ChannelEnabledTimestep(startIndex8, rs232ID8); // Returns the ID of the timestep -1 (note the returned ID corresponds to list index!)
                            if (sequence.TimeSteps.Count > rs232EnabledID8) // An enabled step exists after the selected index
                            {
                                channelData8 = sequence.TimeSteps[rs232EnabledID8].rs232Group.getChannelData(rs232ID8); // Fetch the timestep data
                                nextRS232cmd8 = sequence.timeAtTimestep(rs232EnabledID8); // time at start of timestep
                                startIndex8 = rs232EnabledID8; // or this -1? This is the index at the start of the searc for the next command

                                if (channelData8.DataType == RS232GroupChannelData.RS232DataType.Raw)
                                {
                                    // Raw string commands just get added 
                                    rs232cmd8 = channelData8.RawString;
                                }
                                else if (channelData8.DataType == RS232GroupChannelData.RS232DataType.Parameter)
                                {
                                    if (channelData8.StringParameterStrings != null)
                                    {
                                        foreach (StringParameterString srs in channelData8.StringParameterStrings)
                                        {
                                            rs232cmd8 = srs.ToString().Replace(",", ".");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                RsOutput8 = false; // Last RS232 command for dev 8 has been executed
                            }
                        }
                    }
                    else
                        if (networkClockProvider != null && (currentSeqTime >= (duration * 1000.0)))
                    {
                        break;
                    }
                    Thread.Sleep(100);
                }


                if (softwareClockProvider != null)
                    softwareClockProvider.AbortClockProvider();
                softwareClockProvider = null;
                networkClockProvider.AbortClockProvider();
                networkClockProvider = null;



                MainClientForm.instance.CurrentlyOutputtingTimestep = sequence.dwellWord();



                actionStatus = Storage.settingsData.serverManager.getRunSuccessOnConnectedServers(addMessageLogText);
                if (actionStatus != ServerManager.ServerActionStatus.Success)
                {
                    addMessageLogText(this, new MessageEvent("Run failed, possibly due to a buffer underrun. Please check the server event logs."));
                    ErrorDetected = true;
                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }


                if (useLoops)
                    sequence.cleanupLoopCopies();


                addMessageLogText(this, new MessageEvent("Finished run. Writing log file..."));
                RunLog runLog = new RunLog(runStartTime, formCreationTime, sequence, Storage.settingsData, WordGenerator.MainClientForm.instance.OpenSequenceFileName, WordGenerator.MainClientForm.instance.OpenSettingsFileName);
                string fileName = runLog.WriteLogFile();

                // Added by Jere
                _serialPort.Close();
                serialConnected = false;

                _serialPort2.Close();
                serial2Connected = false;

                _serialPort3.Close();
                serial3Connected = false;

                _serialPort4.Close();
                serial4Connected = false;

                _serialPort5.Close();
                serial5Connected = false;

                _serialPort6.Close();
                serial6Connected = false;

                _serialPort7.Close();
                serial7Connected = false;

                _serialPort8.Close();
                serial8Connected = false;
                // End added by Jere

                if (fileName != null)
                {
                    addMessageLogText(this, new MessageEvent("Log written to " + fileName));
                }
                else
                {
                    addMessageLogText(this, new MessageEvent("Log not written! Perhaps a file with this name already exists?"));
                    ErrorDetected = true;
                }

                foreach (RunLogDatabaseSettings rset in Storage.settingsData.RunlogDatabaseSettings)
                {

                    if (rset.Enabled)
                    {
                        RunlogDatabaseHandler handler = null;
                        try
                        {
                            handler = new RunlogDatabaseHandler(rset);
                            handler.addRunLog(fileName, runLog);
                            addMessageLogText(this, new MessageEvent("Run log added to mysql database at url " + rset.Url + " successfully."));
                        }
                        catch (RunLogDatabaseException e)
                        {
                            addMessageLogText(this, new MessageEvent("Caught exception when attempting to add runlog to mysqldatabase at " + rset.Url + "."));
                            if (rset.VerboseErrorReporting)
                            {
                                addMessageLogText(this, new MessageEvent("Displaying runlogdatabase exception. To disable this display, turn off verbose error reporting for this runlog database in Cicero settings (under Advanced->Settings Explorer)"));
                                ExceptionViewerDialog ev = new ExceptionViewerDialog(e);
                                ev.ShowDialog();
                            }
                            else
                            {
                                addMessageLogText(this, new MessageEvent("Exception was " + e.Message + ". For more detailed information, turn on verbose error reporting for this runlog database in Cicero settings (under Advanced->Settings Explorer)"));
                            }
                        }

                        if (handler != null)
                            handler.closeConnection();
                    }
                }







                if (runRepeat)
                    keepGoing = true;
                else
                    keepGoing = false;

                repeatCount++;

                if (abortAfterThis.Checked)
                {
                    userAborted = true;
                    setStatus(RunFormStatus.FinishedRun);
                    return false;
                }

                setStatus(RunFormStatus.FinishedRun);
            }


            return true;
        }

        private void lic_chk()
        {
            if (WordGenerator.MainClientForm.instance.studentEdition)
                addMessageLogText(this, new MessageEvent("Your Cicero Professional Edition (C) License expired on March 31. You are now running a temporary 24 hour STUDENT EDITION license. Please see http://web.mit.edu/~akeshet/www/Cicero/apr1.html for license renewal information."));
        }

        public bool providerTimerFinished(int priority)
        {
            return true;
        }


        private int currentSoftwareclockPriority = 0;
        public bool reachedTime(UInt32 elapsedTime, int priority)
        {
            if (priority >= currentSoftwareclockPriority)
            {
                currentSoftwareclockPriority = priority;
                Action<int, SequenceData> updateAction = updateProgressBar;
                BeginInvoke(updateAction, new object[] { (int)elapsedTime, runningSequence });
                return true;
            }
            else
                return false;
        }

        public bool handleExceptionOnClockThread(Exception e)
        {
            return false;
        }

        private void updateProgressBar(int elapsed_milliseconds, SequenceData sequence)
        {
            try
            {
                TimeStep step = sequence.getTimeStepAtTime((double)elapsed_milliseconds / 1000.0);

                if (step != null)
                {
                    if (!step.LoopCopy)
                        MainClientForm.instance.CurrentlyOutputtingTimestep = step;
                    else
                        MainClientForm.instance.CurrentlyOutputtingTimestep = step.loopOriginalCopy;
                }
                string stepName = "";
                if (step != null)
                    stepName = step.StepName;

                stepLabel.Text = stepName;

                if (elapsed_milliseconds >= progressBar.Maximum)
                {
                    progressBar.Value = progressBar.Maximum;
                    timeLabel.Text = (progressBar.Maximum / 1000.0) + " s";
                }
                else if (elapsed_milliseconds <= 0)
                {
                    progressBar.Value = 0;
                    timeLabel.Text = "0 s";
                }
                else
                {

                    /// Workaround hack for a dumb bug in the windows 7 progress bar.
                    /// The bar insists on animating "gradually" between various set values, but this causes
                    /// it to be about 1 second out of sync with the run.
                    /// However, when reducing the set value of the bar, animation is instant.
                    /// So this code moves the bar forward past the correct point
                    /// so tha the default code can move it back again (which is instant).
                    if (WordGenerator.GlobalInfo.usingWindows7)
                    {
                        progressBar.Value = Math.Min(progressBar.Maximum, elapsed_milliseconds + 1);
                    }

                    progressBar.Value = elapsed_milliseconds;

                    timeLabel.Text = (elapsed_milliseconds / 1000.0) + " s";
                }
            }
            catch (Exception e)
            {
                addMessageLogText(this, new MessageEvent("Except caught while updating progress bar: " + e.Message + e.StackTrace));
            }
        }

        private void initializeProgressBar(double duration)
        {
            progressBar.Value = 0;
            progressBar.Maximum = (int)(duration * 1000.0);
            durationLabel.Text = duration + " s";
            timeLabel.Text = "0 s";
        }


        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (softwareClockProvider != null)
                softwareClockProvider.AbortClockProvider();
            softwareClockProvider = null;

            if (networkClockProvider != null)
                networkClockProvider.AbortClockProvider();
            networkClockProvider = null;

            WordGenerator.MainClientForm.instance.suppressHotkeys = false;
            if (this.isBackgroundRunform)
            {
                RunForm.backgroundRunningRunform = null;
                if (this.backgroundRunUpdated != null)
                {
                    this.backgroundRunUpdated(this, null);
                }
            }

        }

        private void closeButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void stopButton_Click(object sender, EventArgs e)
        {
            userAborted = true;
            foreach (Socket theSocket in CameraPCsSocketList)
            {
                try
                {

                    theSocket.Send(Encoding.ASCII.GetBytes("Abort"), 0, Encoding.ASCII.GetBytes("Abort").Length, SocketFlags.None);
                }
                catch { }
            }

            // Added by Jere
            if (serialConnected)
            {
                _serialPort.Close();
                serialConnected = false;
            }
            if (serial2Connected)
            {
                _serialPort2.Close();
                serial2Connected = false;
            }
            if (serial3Connected)
            {
                _serialPort3.Close();
                serial3Connected = false;
            }
            if (serial4Connected)
            {
                _serialPort4.Close();
                serial4Connected = false;
            }
            if (serial5Connected)
            {
                _serialPort5.Close();
                serial5Connected = false;
            }
            if (serial6Connected)
            {
                _serialPort6.Close();
                serial6Connected = false;
            }
            if (serial7Connected)
            {
                _serialPort7.Close();
                serial7Connected = false;
            }
            if (serial8Connected)
            {
                _serialPort8.Close();
                serial8Connected = false;
            }
            // End added by Jere

            //Give time for the dwell word to be sent
            isIdle = true;
            this.runAgainButton.Enabled = false;
            System.Timers.Timer idleTimer = new System.Timers.Timer(1000);
            idleTimer.SynchronizingObject = this;
            idleTimer.Elapsed += new System.Timers.ElapsedEventHandler(idleTimer_Elapsed);
            idleTimer.Start();

            if (softwareClockProvider != null)
                softwareClockProvider.AbortClockProvider();
            softwareClockProvider = null;


            if (networkClockProvider != null)
                networkClockProvider.AbortClockProvider();
            networkClockProvider = null;


            if (this.runningThread != null)
                runningThread.Abort();
            addMessageLogText(this, new MessageEvent("Run aborting."));
            Storage.settingsData.serverManager.stopAllServers(addMessageLogText);
            addMessageLogText(this, new MessageEvent("Run aborted."));



            TimeStep step;
            if (isBackgroundRunform)
                step = Storage.sequenceData.dwellWord();
            else
                step = runningSequence.dwellWord();
            if (step != null)
            {
                addMessageLogText(this, new MessageEvent("Attempting to output the dwell timestep."));
                bool success = false;

                success = ClientRunner.instance.outputTimestepNow(step, false, false);


                if (success)
                {
                    addMessageLogText(this, new MessageEvent("Dwell output successfull."));
                }
                else
                {
                    addMessageLogText(this, new MessageEvent("Dwell output unsuccessfull."));
                    ErrorDetected = true;
                }
            }
            this.setStatus(RunFormStatus.FinishedRun);
        }

        private void runAgainButton_Click(object sender, EventArgs e)
        {
            startRun();
        }




        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(
            IntPtr hWnd, // handle to window    
            int id, // hot key identifier    
            KeyModifiers fsModifiers, // key-modifier options    
            Keys vk    // virtual-key code    
            );

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id
            );


        public enum KeyModifiers        //enum to call 3rd parameter of RegisterHotKey easily
        {
            None = 0,
            Alt = 1,
            Control = 2,
            Shift = 4,
            Windows = 8
        }


        const int WM_HOTKEY = 0x0312;

        protected override void WndProc(ref Message m)
        {

            switch (m.Msg)
            {
                // handle hotkey, based on the type of object bound to it
                case WM_HOTKEY:
                    {


                        int id = (int)m.WParam;
                        if (hotkeyButtons.ContainsKey(id))
                        {
                            Button bt = hotkeyButtons[id];
                            if (bt != null)
                                bt.PerformClick();
                        }


                    }
                    break;
            }


            base.WndProc(ref m);

        }

        private void RunForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Storage.settingsData.CameraPCs.Count != 0)
            {
                getConfirmationThread.Abort();
            }

            foreach (Socket theSocket in CameraPCsSocketList)
            {
                try
                {

                    theSocket.Send(Encoding.ASCII.GetBytes("Closing"), 0, Encoding.ASCII.GetBytes("Closing").Length, SocketFlags.None);
                }
                catch { }
            }
            if (!closeButton.Enabled)
            {
                e.Cancel = true;
            }

            runningSequence.cleanupLoopCopies();

            if (variablePreviewForm != null)
                variablePreviewForm.Close();
        }


        private void RunForm_Activated(object sender, EventArgs e)
        {
            registerAllHotkeys();
        }

        private void RunForm_Deactivate(object sender, EventArgs e)
        {
            unregisterAllHotkeys();
        }

        //Receives log messages from the camera software. Only runs if a camera is listed
        private void getConfirmationEntryPoint()
        {
            string[] conf;
            byte[] bconf;
            while (true)
            {
                Thread.Sleep(1);
                foreach (Socket theSocket in CameraPCsSocketList)
                {
                    try
                    {
                        if (theSocket.Available > 0)
                        {
                            bconf = new byte[theSocket.Available];
                            theSocket.Receive(bconf, 0, bconf.Length, SocketFlags.None);
                            conf = (Encoding.ASCII.GetString(bconf)).Split('.');
                            for (int i = 0; conf.Length > i; i++)
                            {
                                if (conf[i] != "")
                                    addMessageLogText(this, new MessageEvent(conf[i] + "."));
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        //ReEnables the Run Again Button
        private void idleTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            this.runAgainButton.Enabled = true;
            isIdle = false;
            (sender as System.Timers.Timer).Stop();
        }

        private void textBox1_Click(object sender, EventArgs e)
        {
            this.ErrorDetected = false;
        }

        private WordGenerator.Controls.VariablePreviewEditorForm variablePreviewForm;

        private void showVariablePreviewCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            if (!this.isBackgroundRunform)
            {
                if (showVariablePreviewCheckbox.Checked)
                {
                    variablePreviewForm = new Controls.VariablePreviewEditorForm(runningSequence.Variables);
                    variablePreviewForm.FormClosed += new FormClosedEventHandler(variablePreviewForm_FormClosed);
                    variablePreviewForm.Show();
                }
                else
                {
                    if (variablePreviewForm != null)
                        variablePreviewForm.Close();
                    variablePreviewForm = null;
                }
            }
        }

        void variablePreviewForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            variablePreviewForm = null;
            showVariablePreviewCheckbox.Checked = false;
        }


        #region Background running
        private static RunForm backgroundRunningRunform = null;
        private bool isBackgroundRunform = false;
        private event EventHandler backgroundRunUpdated;
        private bool userAborted = false;
        public static void beginBackgroundRunAsLoop(SequenceData sequenceToRun, RunType runtype, bool runRepeat, EventHandler updateCallback)
        {
            backgroundRunningRunform = new RunForm(sequenceToRun, runtype, runRepeat);
            backgroundRunningRunform.isBackgroundRunform = true;
            backgroundRunningRunform.showVariablePreviewCheckbox.Visible = false;
            backgroundRunningRunform.backgroundRunUpdated += updateCallback;
            backgroundRunningRunform.Show();
        }

        public static bool backgroundIsRunning()
        {
            return RunForm.backgroundRunningRunform != null;
        }

        public static void bringBackgroundRunFormToFront()
        {
            if (RunForm.backgroundRunningRunform != null)
            {
                RunForm.backgroundRunningRunform.BringToFront();
                RunForm.backgroundRunningRunform.Focus();
            }
        }

        public static void abortAtEndOfNextBackgroundRun()
        {
            if (backgroundRunningRunform != null)
            {
                Action enableAbortCheck = () => backgroundRunningRunform.abortAfterThis.Checked = true;
                backgroundRunningRunform.BeginInvoke(enableAbortCheck);
            }
        }
        #endregion
    }
}
