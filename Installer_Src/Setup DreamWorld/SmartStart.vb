﻿#Region "Copyright AGPL3.0"

' Copyright Outworldz, LLC. AGPL3.0 https://opensource.org/licenses/AGPL

#End Region

Imports System.IO
Imports System.Text.RegularExpressions

Module SmartStart
    Public ReadOnly BootedList As New List(Of String)
    Public ReadOnly ProcessIdDict As New Dictionary(Of Integer, Process)

    Public Sub BuildLand(Avatars As Dictionary(Of String, String))

        If Not Settings.AutoFill Then Return
        If Avatars.Count = 0 Then Return

        For Each Agent In Avatars
            If Agent.Value.Length > 0 Then

                Dim RegionUUID = Agent.Value
                Dim RegionName As String

                RegionName = Region_Name(RegionUUID)
                If RegionName Is Nothing Then Continue For

                If RegionName.Length > 0 Then
                    Dim X = Coord_X(RegionUUID)
                    Dim Y = Coord_Y(RegionUUID)
                    If X = 0 Or Y = 0 Then Continue For

                    Try
                        SurroundingLandMaker(RegionUUID)
                    Catch ex As Exception
                        BreakPoint.Dump(ex)
                    End Try

                End If
            Else
                BreakPoint.Print("Region Name cannot be located")
            End If

        Next

    End Sub

    Public Function GetAllAgents() As Dictionary(Of String, String)

        ' Scan all the regions
        Dim FakeAgents = GetAgentList()
        Dim AllAgents = GetGridUsers()
        Dim Presence = GetPresence()

        For Each item In FakeAgents
            If AllAgents.ContainsKey(item.Key) Then
                AllAgents.Item(item.Key) = item.Value
            Else
                AllAgents.Add(item.Key, item.Value)
            End If
        Next

        For Each item In Presence
            If AllAgents.ContainsKey(item.Key) Then
                AllAgents.Item(item.Key) = item.Value
            Else
                AllAgents.Add(item.Key, item.Value)
            End If

        Next

        Return AllAgents

    End Function

    Public Sub SequentialPause()

        ''' <summary>
        ''' 0 for no waiting
        ''' 1 for Sequential
        ''' 2 for concurrent
        ''' ''' </summary>
        '''
        If Settings.SequentialMode = 0 Then
            Return
        ElseIf Settings.SequentialMode = 1 Then
            Dim ctr = 5 * 60  ' 5 minute max to start a region
            While True
                If Not PropOpensimIsRunning Then Return
                Dim wait As Boolean = False
                For Each RegionUUID As String In RegionUuids()

                    ' see if there is a window still open. If so, its running
                    If Not PropAborting And CBool(GetHwnd(Group_Name(RegionUUID))) Then
                        'Diagnostics.Debug.Print($"Waiting On {Region_Name(RegionUUID)}")
                        wait = True
                    Else
                        'Diagnostics.Debug.Print($"{GetStateString(RegionStatus(RegionUUID))} {Region_Name(RegionUUID)}")
                    End If

                Next

                If wait Then
                    ctr -= 1
                Else
                    Exit While
                End If
                If ctr <= 0 Then
                    Exit While
                End If
                Sleep(1000)
            End While

        ElseIf Settings.SequentialMode = 2 Then ' Concurrent mode

            If Not Settings.BootOrSuspend Then
                Return
            End If

            Dim ctr = 5 * 60 ' 5 minute max to start a region at 100% CPU
            While True

                If Not PropOpensimIsRunning Then Return

                If (FormSetup.CPUAverageSpeed < Settings.CPUMAX AndAlso Settings.Ramused < 90) _
                    Or Settings.BootOrSuspend = False Then

                    Exit While
                End If
                Sleep(1000)
                Application.DoEvents()
                ctr -= 1
                If ctr <= 0 Then
                    Exit While
                End If
            End While

        End If

    End Sub

    Public Sub TeleportClicked(Regionuuid As String)

        Dim RegionName = Region_Name(Regionuuid)

        'secondlife://http|!!hg.osgrid.org|80+Lbsa+Plaza

        Dim link = "secondlife://http|!!" & Settings.PublicIP & "|" & Settings.HttpPort & "+" & RegionName
        Try
            System.Diagnostics.Process.Start(link)
        Catch ex As Exception
            BreakPoint.Dump(ex)
        End Try

    End Sub

#Region "StartStart"

    ''' <summary>
    ''' Stops and deletes a region and all the DB things it came with
    ''' </summary>
    ''' <param name="RegionUUID"></param>
    Public Sub DeleteAllRegionData(RegionUUID As String)

        Dim RegionName = Region_Name(RegionUUID)
        Dim GroupName = Group_Name(RegionUUID)
        PropAborting = True
        ShutDown(RegionUUID, SIMSTATUSENUM.ShuttingDownForGood)
        ' wait 2 minute for the region to quit
        Dim ctr = 120

        While PropOpensimIsRunning AndAlso RegionStatus(RegionUUID) <> SIMSTATUSENUM.Stopped And
             RegionStatus(RegionUUID) <> SIMSTATUSENUM.Error
            Sleep(1000)
            ctr -= 1
            If ctr = 0 Then Exit While
        End While

        DeleteAllContents(RegionUUID)
        PropAborting = False
        PropChangedRegionSettings = True
        PropUpdateView = True

    End Sub

    Private Function AddEm(RegionUUID As String, AgentID As String) As Boolean

        If RegionUUID = "00000000-0000-0000-0000-000000000000" Then
            BreakPoint.Print("UUID Zero")
            Logger("Addem", "Bad UUID", "Teleport")
            Return True
        End If

        Dim result As New Guid
        If Not Guid.TryParse(RegionUUID, result) Then
            Logger("Addem", "Bad UUID", "Teleport")
            Return False
        End If

        Logger("Teleport Request", Region_Name(RegionUUID) & ":" & AgentID, "Teleport")

        If TeleportAvatarDict.ContainsKey(AgentID) Then
            TeleportAvatarDict.Remove(AgentID)
        End If
        TeleportAvatarDict.Add(AgentID, RegionUUID)
        Bench.Print("Teleport Added")

        ReBoot(RegionUUID) ' Wait for it to start booting

        Bench.Print("Reboot Signaled")
        Return False

    End Function

#End Region

#Region "HTML"

    Public Function RegionListHTML(Data As String) As String

        ' there is only a 16KB capability in Opensim for reading a web page.
        ' so we have to ask for a 1-32, 2-64 size chunks for larger grids

        ' Added Start and End integers
        '  ?Start=1&End=32

        Dim startRegion As Integer = 1
        Dim Count As Integer = 256 ' a default for older signs

        Dim pattern = New Regex("Start=(\d+?)&Count=(\d+)", RegexOptions.IgnoreCase)
        Dim match As Match = pattern.Match(Data)
        If match.Success Then
            Integer.TryParse(Uri.UnescapeDataString(match.Groups(1).Value), startRegion)
            Integer.TryParse(Uri.UnescapeDataString(match.Groups(2).Value), Count)
        End If

        ' http://localhost:8001/teleports.htm
        ' http://YourURL:8001/teleports.htm
        'Outworldz|Welcome||outworldz.com:9000:Welcome|128,128,96|
        '*|Welcome||outworldz.com9000Welcome|128,128,96|
        Dim HTML As String = ""

        ' whole lotta sorting going on as the RegionUUID list is not sorted.
        Dim ToSort As New List(Of String)
        For Each RegionUUID As String In RegionUuids()
            ToSort.Add(Region_Name(RegionUUID))
        Next
        ToSort.Sort() ' not it is sorted

        ' but we want Welcome at the very beginning of the 1st sign
        Dim NewSort As New List(Of String)
        If startRegion = 1 Then        ' first sign
            NewSort.Add(Settings.WelcomeRegion)
        Else
            startRegion -= 1
        End If

        For Each item In ToSort
            If item <> Settings.WelcomeRegion Then
                NewSort.Add(item)
            End If
        Next

        Dim ctr = 1
        Dim used = 1
        For Each RegionName In NewSort

            Dim RegionUUID = FindRegionByName(RegionName)

            ' only print the ones inclusive between startRegion and lastRegion
            If ctr >= startRegion And used <= Count Then
                If Teleport_Sign(RegionUUID) = "True" AndAlso RegionEnabled(RegionUUID) Then
                    HTML += $"*|{RegionName}||{Settings.PublicIP}:{Settings.HttpPort}:{RegionName}||{RegionName}|{vbCrLf}"
                    used += 1
                End If
            End If
            ctr += 1
        Next

        Dim HTMLFILE = Settings.OpensimBinPath & "data\teleports.htm"
        DeleteFile(HTMLFILE)

        Try
            Using outputFile As New StreamWriter(HTMLFILE, False)
                outputFile.WriteLine(HTML)
            End Using
        Catch ex As Exception
        End Try

        Return HTML

    End Function

    Public Function SmartStartParse(post As String) As String

        ' Smart Start AutoStart Region mode
        Debug.Print("Smart Start:" + post)

        'Smart Start:http://192.168.2.140:8999/?alt=Deliverance_of_JarJar_Binks__Fred_Beckhusen_1X1&agent=Ferd%20Frederix&AgentID=6f285c43-e656-42d9-b0e9-a78684fee15d&password=XYZZY

        Dim pattern = New Regex("alt=(.*?)&agent=(.*?)&agentid=(.*?)&password=(.*)", RegexOptions.IgnoreCase)
        Dim match As Match = pattern.Match(post)
        If match.Success Then
            Dim Name As String = Uri.UnescapeDataString(match.Groups(1).Value)
            'Debug.Print($"Name={Name}")
            Dim TeleportType As String = Uri.UnescapeDataString(match.Groups(2).Value)
            'Debug.Print($"TeleportType={TeleportType}")
            Dim AgentID As String = Uri.UnescapeDataString(match.Groups(3).Value)
            'Debug.Print($"AgentID={AgentID}")
            Dim Password As String = Uri.UnescapeDataString(match.Groups(4).Value)
            'Debug.Print($"Password={Password}")
            If Password <> Settings.MachineID Then
                Logger("ERROR", $"Bad Password {Password} for Teleport system. Should be the Dyn DNS password.", "Outworldz")
                Return ""
            End If

            Dim time As String

            ' Region may be a name or a Region UUID
            Dim RegionUUID = FindRegionByName(Name)
            If RegionUUID.Length = 0 Then
                RegionUUID = Name ' Its a UUID
            Else
                Name = Region_Name(RegionUUID)
            End If
            'Debug.Print("Teleport to " & Name)

            ' Smart Start below here

            If Smart_Start(RegionUUID) = "True" AndAlso Settings.Smart_Start Then

                ' smart, and up
                If RegionEnabled(RegionUUID) Then
                    If RegionStatus(RegionUUID) = SIMSTATUSENUM.Booted Then
                        If TeleportType.ToUpperInvariant = "UUID" Then
                            'Logger("UUID Teleport", Name & ":" & AgentID, "Teleport")
                            Return RegionUUID
                        ElseIf TeleportType.ToUpperInvariant = "REGIONNAME" Then
                            'Logger("Named Teleport", Name & ":" & AgentID, "Teleport")
                            Return Name
                        Else ' Its a sign!
                            ' Logger("Teleport Sign Booted", Name & ":" & AgentID, "Teleport")
                            Return Name & "|0"
                        End If
                    Else  ' requires booting

                        If TeleportType.ToUpperInvariant = "UUID" Then
                            'Logger("UUID Teleport", Name & ":" & AgentID, "Teleport")
                            AddEm(RegionUUID, AgentID)

                            If Settings.BootOrSuspend Then
                                RPC_admin_dialog(AgentID, $"Booting your region {Region_Name(RegionUUID)}.{vbCrLf}Region will be ready in {CStr(BootTime(RegionUUID) + Settings.TeleportSleepTime)} seconds. Please wait in this region.")
                            End If

                            Dim u = FindRegionByName(Settings.ParkingLot)
                            Return u
                        ElseIf TeleportType.ToUpperInvariant = "REGIONNAME" Then
                            Logger("Named Teleport", Name & ":" & AgentID, "Teleport")
                            AddEm(RegionUUID, AgentID)
                            If Settings.BootOrSuspend Then
                                RPC_admin_dialog(AgentID, $"Booting your region { Region_Name(RegionUUID)}.{vbCrLf}Region will be ready in {CStr(BootTime(RegionUUID) + Settings.TeleportSleepTime)} seconds. Please wait in this region.")
                            End If

                            Return FindRegionByName(Settings.ParkingLot)
                        Else ' Its a v4 sign

                            If Settings.MapType = "None" AndAlso MapType(RegionUUID).Length = 0 Then
                                time = "|" & CStr(BootTime(RegionUUID) + Settings.TeleportSleepTime)
                            Else
                                time = "|" & CStr(MapTime(RegionUUID) + Settings.TeleportSleepTime)
                            End If
                            If Settings.BootOrSuspend Then
                                RPC_admin_dialog(AgentID, $"Booting your region { Region_Name(RegionUUID)}.{vbCrLf}Region will be ready in {CStr(time)} seconds.")
                            End If

                            Logger("Agent ", Name & ":" & AgentID, "Teleport")
                            AddEm(RegionUUID, AgentID)
                            Return Settings.ParkingLot

                        End If
                    End If
                Else
                    ' not enabled
                    RPC_admin_dialog(AgentID, $"Your region { Region_Name(RegionUUID)} is disabled.")
                End If
            Else ' Non Smart Start

                If TeleportType.ToUpperInvariant = "UUID" Then
                    Logger("Teleport Non Smart", Name & ":" & AgentID, "Teleport")
                    Return RegionUUID
                ElseIf TeleportType.ToUpperInvariant = "REGIONNAME" Then
                    'Logger("Teleport Non Smart", Name & ":" & AgentID, "Teleport")
                    Return Name
                Else     ' Its a sign!
                    'Logger("Teleport Sign ", Name & ":" & AgentID, "Teleport")
                    AddEm(RegionUUID, AgentID)
                    Return Name
                End If
            End If
        End If

        Return FindRegionByName(Settings.WelcomeRegion)

    End Function

#End Region

#Region "BootUp"

    ReadOnly BootupLock As New Object

    Public Function Boot(BootName As String) As Boolean
        ''' <summary>Starts Opensim for a given name</summary>
        ''' <param name="BootName">Name of region to start</param>
        ''' <returns>success = true</returns>
        '''
        SyncLock BootupLock

            PropOpensimIsRunning() = True
            If PropAborting Then Return True

            Dim RegionUUID As String = FindRegionByName(BootName)
            If Not RegionEnabled(RegionUUID) Then Return True
            Dim GroupName = Group_Name(RegionUUID)

            If String.IsNullOrEmpty(RegionUUID) Then
                ErrorLog("Cannot find " & BootName & " to boot!")
                Return False
            End If

            SetCores(RegionUUID)

            ' Detect if a region Window is already running
            If CBool(GetHwnd(Group_Name(RegionUUID))) Then

                If RegionStatus(RegionUUID) = SIMSTATUSENUM.Suspended Then
                    Logger("Suspended, Resuming it", BootName, "Teleport")

                    Dim PID As Integer = GetPIDofWindow(GroupName)

                    If Not PropInstanceHandles.ContainsKey(PID) Then
                        PropInstanceHandles.Add(PID, GroupName)
                    End If

                    If Settings.BootOrSuspend Then
                        For Each UUID As String In RegionUuidListByName(GroupName)
                            RegionStatus(UUID) = SIMSTATUSENUM.Resume
                            ProcessID(UUID) = PID
                            SendToOpensimWorld(UUID, 0)
                        Next
                    Else
                        For Each UUID As String In RegionUuidListByName(GroupName)
                            ResumeRegion(UUID)
                            RegionStatus(UUID) = SIMSTATUSENUM.Booted
                            ProcessID(UUID) = PID
                            SendToOpensimWorld(UUID, 0)
                        Next
                    End If

                    ShowDOSWindow(GetHwnd(Group_Name(RegionUUID)), MaybeShowWindow())
                    Logger("Info", "Region " & BootName & " skipped as it is Suspended, Resuming it instead", "Teleport")
                    PropUpdateView = True ' make form refresh
                    Return True
                Else    ' needs to be captured into the event handler

                    ' TextPrint(BootName & " " & My.Resources.Running_word)
                    Dim PID As Integer = GetPIDofWindow(GroupName)
                    If Not PropInstanceHandles.ContainsKey(PID) Then
                        PropInstanceHandles.Add(PID, GroupName)
                    End If

                    For Each UUID As String In RegionUuidListByName(GroupName)
                        'Must be listening, not just in a window
                        ResumeRegion(UUID)
                        If CheckPort("127.0.0.1", GroupPort(RegionUUID)) Then
                            RegionStatus(UUID) = SIMSTATUSENUM.Booted
                            SendToOpensimWorld(RegionUUID, 0)
                        End If

                        ProcessID(UUID) = PID
                        Application.DoEvents()
                    Next
                    ShowDOSWindow(GetHwnd(Group_Name(RegionUUID)), MaybeHideWindow())

                    PropUpdateView = True ' make form refresh
                    Return True
                End If

            End If

            TextPrint(BootName & " " & Global.Outworldz.My.Resources.Starting_word)

            DoCurrency()

            If CopyOpensimProto(RegionUUID) Then Return False

#Disable Warning CA2000 ' Dispose objects before losing scope
            Dim BootProcess = New Process With {
                .EnableRaisingEvents = True
            }
#Enable Warning CA2000 ' Dispose objects before losing scope

            BootProcess.StartInfo.UseShellExecute = True
            BootProcess.StartInfo.WorkingDirectory = Settings.OpensimBinPath()
            BootProcess.StartInfo.FileName = """" & Settings.OpensimBinPath() & "OpenSim.exe" & """"
            BootProcess.StartInfo.CreateNoWindow = False

            Select Case Settings.ConsoleShow
                Case "True"
                    BootProcess.StartInfo.WindowStyle = ProcessWindowStyle.Normal
                Case "False"
                    BootProcess.StartInfo.WindowStyle = ProcessWindowStyle.Normal
                Case "None"
                    BootProcess.StartInfo.WindowStyle = ProcessWindowStyle.Minimized
            End Select

            BootProcess.StartInfo.Arguments = " -inidirectory=" & """" & "./Regions/" & GroupName & """"

            Environment.SetEnvironmentVariable("OSIM_LOGPATH", Settings.OpensimBinPath() & "Regions\" & GroupName)

            SequentialPause()   ' wait for previous region to give us some CPU

            Dim ok As Boolean = False
            Try
                ok = BootProcess.Start
                Application.DoEvents()
            Catch ex As Exception
                ErrorLog(ex.Message)
            End Try

            If ok Then
                Dim PID = WaitForPID(BootProcess)           ' check if it gave us a PID, if not, it failed.

                If ProcessIdDict.ContainsKey(PID) Then
                    ProcessIdDict.Item(PID) = Process.GetProcessById(PID)
                Else
                    ProcessIdDict.Add(PID, BootProcess)
                End If

                If PID > 0 Then
                    ' 0 is all cores
                    Try
                        If Cores(RegionUUID) > 0 Then
                            BootProcess.ProcessorAffinity = CType(Cores(RegionUUID), IntPtr)
                        End If
                    Catch ex As Exception
                        BreakPoint.Dump(ex)
                    End Try

                    Try
                        Dim Pri = Priority(RegionUUID)

                        Dim E = New PRIEnumClass
                        Dim P As ProcessPriorityClass
                        If Pri = "RealTime" Then
                            P = E.RealTime
                        ElseIf Pri = "High" Then
                            P = E.High
                        ElseIf Pri = "AboveNormal" Then
                            P = E.AboveNormal
                        ElseIf Pri = "Normal" Then
                            P = E.Normal
                        ElseIf Pri = "BelowNormal" Then
                            P = E.BelowNormal
                        Else
                            P = E.Normal
                        End If

                        BootProcess.PriorityClass = P
                    Catch ex As Exception
                        BreakPoint.Dump(ex)
                    End Try

                    If Not SetWindowTextCall(BootProcess, GroupName) Then
                        ShutDown(RegionUUID, SIMSTATUSENUM.Error)
                    End If

                    If Not PropInstanceHandles.ContainsKey(PID) Then
                        PropInstanceHandles.Add(PID, GroupName)
                    End If

                    ' Mark them before we boot as a crash will immediately trigger the event that it exited
                    For Each UUID As String In RegionUuidListByName(GroupName)
                        RegionStatus(UUID) = SIMSTATUSENUM.Booting
                        PokeRegionTimer(RegionUUID)
                    Next

                    AddCPU(PID, GroupName) ' get a list of running opensim processes
                    For Each UUID As String In RegionUuidListByName(GroupName)
                        ProcessID(UUID) = PID
                    Next
                Else
                    BreakPoint.Print("No PID for " & GroupName)
                End If

                PropUpdateView = True ' make form refresh
                FormSetup.Buttons(FormSetup.StopButton)
                Return True
            End If
            PropUpdateView = True ' make form refresh
            Logger("Failed to boot ", BootName, "Teleport")
            TextPrint("Failed to boot region " & BootName)
            Return False
        End SyncLock

    End Function

    Public Sub ReBoot(RegionUUID As String)

        If RegionStatus(RegionUUID) = SIMSTATUSENUM.Suspended Or
                 RegionStatus(RegionUUID) = SIMSTATUSENUM.Stopped Or
                 RegionStatus(RegionUUID) = SIMSTATUSENUM.Error Or
                 RegionStatus(RegionUUID) = SIMSTATUSENUM.ShuttingDownForGood Then

            For Each RegionUUID In RegionUuidListByName(Group_Name(RegionUUID))
                RegionStatus(RegionUUID) = SIMSTATUSENUM.Resume
                PokeRegionTimer(RegionUUID)
            Next
            PropUpdateView = True ' make form refresh
        ElseIf RegionStatus(RegionUUID) = SIMSTATUSENUM.Booted Then
            FormSetup.RunTaskList(RegionUUID)
        End If

    End Sub

#End Region

#Region "Pass2"

    Public Sub Apply_Plant(RegionUUID As String)

        Dim RegionName = Region_Name(RegionUUID)
        RPC_Region_Command(RegionUUID, $"change region ""{RegionName}""")

        If RegionUUID.Length > 0 Then
            Dim R As New RegionEssentials With {
             .RegionUUID = RegionUUID,
             .RegionName = RegionName
             }

            GenTrees(R)
        End If

    End Sub

    Public Sub ApplyTerrainEffect(RegionUUID As String)

        Dim RegionName = Region_Name(RegionUUID)

        If Not RPC_Region_Command(RegionUUID, $"change region ""{RegionName}""") Then Return

        Dim backupname = IO.Path.Combine(Settings.OpensimBinPath, "Terrains")
        If Not RPC_Region_Command(RegionUUID, $"terrain save ""{backupname}\{RegionName}-Backup.r32""") Then Return
        If Not RPC_Region_Command(RegionUUID, $"terrain save ""{backupname}\{RegionName}-Backup.raw""") Then Return
        If Not RPC_Region_Command(RegionUUID, $"terrain save ""{backupname}\{RegionName}-Backup.jpg""") Then Return
        If Not RPC_Region_Command(RegionUUID, $"terrain save ""{backupname}\{RegionName}-Backup.png""") Then Return
        If Not RPC_Region_Command(RegionUUID, $"terrain save ""{backupname}\{RegionName}-Backup.ter""") Then Return

        Dim R As New RegionEssentials With {
             .RegionUUID = RegionUUID,
             .RegionName = RegionName
             }

        GenLand(R)

    End Sub

    Public Sub Bake_Terrain(RegionUUID As String)

        Dim Name = Region_Name(RegionUUID)
        RPC_Region_Command(RegionUUID, $"change region ""{Name}""")
        RPC_Region_Command(RegionUUID, "terrain bake")

    End Sub

    Public Sub Delete_Tree(RegionUUID As String)

        For Each TT As String In TreeList
            If Not RPC_Region_Command(RegionUUID, $"tree remove {TT}") Then Return
        Next

    End Sub

    Public Sub Load_AllFreeOARs(RegionUUID As String, obj As TaskObject)

        Dim RegionName = Region_Name(RegionUUID)
        Dim File = obj.Command

        RegionStatus(RegionUUID) = SIMSTATUSENUM.NoError
        TextPrint($"{RegionName} load oar {File}")
        ConsoleCommand(RegionUUID, $"change region ""{RegionName}""", True)
        ConsoleCommand(RegionUUID, $"load oar --force-terrain --force-parcels ""{File}""")

        If Not AvatarsIsInGroup(Group_Name(RegionUUID)) Then
            RegionStatus(RegionUUID) = SIMSTATUSENUM.ShuttingDownForGood
            ConsoleCommand(RegionUUID, "q", True)
        End If

    End Sub

    Public Sub Load_Save(RegionUUID As String)

        Dim RegionName = Region_Name(RegionUUID)
        RPC_Region_Command(RegionUUID, $"change region ""{RegionName}""")

        Dim Terrainfolder = IO.Path.Combine(Settings.OpensimBinPath, "Terrains")
        ' Create an instance of the open file dialog box. Set filter options and filter index.
        Using openFileDialog1 = New OpenFileDialog With {
            .InitialDirectory = Terrainfolder,
            .Filter = Global.Outworldz.My.Resources.OAR_Load_and_Save & "(*.png,*.r32,*.raw, *.ter)|*.png;*.r32;*.raw;*.ter|All Files (*.*)|*.*",
            .FilterIndex = 1,
            .Multiselect = False
            }

            ' Call the ShowDialog method to show the dialog box.
            Dim UserClickedOK As DialogResult = openFileDialog1.ShowDialog

            ' Process input if the user clicked OK.
            If UserClickedOK = DialogResult.OK Then

                Dim thing = openFileDialog1.FileName
                If thing.Length > 0 Then
                    RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}-Backup.r32""")
                    RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}-Backup.raw""")
                    RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}-Backup.jpg""")
                    RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}-Backup.png""")
                    RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}-Backup.ter""")
                    RPC_Region_Command(RegionUUID, $"terrain load ""{thing}""")
                End If
            End If

        End Using

    End Sub

    Public Sub RebuildTerrain(RegionUUId As String)

        Dim Terrainfolder = IO.Path.Combine(Settings.OpensimBinPath, "Terrains")
        Dim exts As New List(Of String) From {
                "*.r32",
                "*.raw",
                "*.ter",
                "*.png"
            }

        For Each extension In exts
            Dim Files = System.IO.Directory.EnumerateFiles(Terrainfolder, extension, SearchOption.TopDirectoryOnly)
            For Each File In Files
                Maketypes(extension, File, RegionUUId)
            Next
        Next

    End Sub

    Public Sub Revert(RegionUUID As String)

        Dim Name = Region_Name(RegionUUID)
        RPC_Region_Command(RegionUUID, $"change region ""{Name}""")
        RPC_Region_Command(RegionUUID, "terrain revert")

    End Sub

    Public Sub Save_Terrain(RegionUUID As String)

        Dim RegionName = Region_Name(RegionUUID)
        Dim Terrainfolder = IO.Path.Combine(Settings.OpensimBinPath, "Terrains")
        Dim S As Double = SizeX(RegionUUID)
        S /= 256
        If S > 1 Then
            Dim path = $"{Terrainfolder}\{S}x{S}"
            ' If the destination folder don't exist then create it
            If Not System.IO.Directory.Exists(path) Then
                MakeFolder(path)
            End If
            Terrainfolder = path
        End If

        If Not RPC_Region_Command(RegionUUID, $"change region {RegionName}") Then Return
        RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}.r32""")
        RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}.raw""")
        RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}.jpg""")
        RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}.png""")
        RPC_Region_Command(RegionUUID, $"terrain save ""{Terrainfolder}\{RegionName}.ter""")

    End Sub

    Private Sub Maketypes(startWith As String, Filename As String, RegionUUID As String)

        Dim Terrainfolder = IO.Path.Combine(Settings.OpensimBinPath, "Terrains")
        Dim extension = IO.Path.GetExtension(Filename)

        Dim Rname = Region_Name(RegionUUID)
        RPC_Region_Command(RegionUUID, $"change region ""{Rname}""")

        Dim RegionName = Filename
        RegionName = RegionName.Replace($"{extension}", startWith)
        RegionName = RegionName.Replace("*", "")
        RPC_Region_Command(RegionUUID, $"terrain load ""{Filename}""")

        RegionName = RegionName.Replace($"{extension}", "")

        Save(RegionUUID, $"terrain save ""{RegionName}.r32""")
        Save(RegionUUID, $"terrain save ""{RegionName}.raw""")
        Save(RegionUUID, $"terrain save ""{RegionName}.jpg""")
        Save(RegionUUID, $"terrain save ""{RegionName}.png""")
        Save(RegionUUID, $"terrain save ""{RegionName}.ter""")

    End Sub

    Private Sub Save(RegionUUID As String, S As String)

        If SavedAlready.Contains(S) Then
            Return
        End If
        RPC_Region_Command(RegionUUID, S)
        SavedAlready.Add(S)

    End Sub

#End Region

End Module
