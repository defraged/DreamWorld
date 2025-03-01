﻿Module Backup

#Region "Backups"

    Public Function BackupPath() As String

        'Autobackup must exist. if not create it
        ' if they set the folder somewhere else, it may have been deleted, so reset it to default
        BackupPath = Settings.BackupFolder
        BackupPath = BackupPath.Replace("\", "/")    ' because Opensim uses Unix-like slashes, that's why

        If Not IO.Directory.Exists(BackupPath) Then
            BackupPath = IO.Path.Combine(Settings.CurrentSlashDir, "OutworldzFiles/Autobackup")
            FileIO.FileSystem.CreateDirectory(BackupPath)
            Settings.BackupFolder = BackupPath
        End If

        Return BackupPath

    End Function

#End Region

#Region "Tasks"

    Public Sub BackupAllRegions()

        Dim L = RegionUuids()
        L.Sort()

        For Each RegionUUID As String In L
            If Not RegionEnabled(RegionUUID) Then Continue For
            If SkipAutobackup(RegionUUID) = "True" Then Continue For

            Dim Obj = New TaskObject With {
                .TaskName = FormSetup.TaskName.RPCBackupper,
                .Command = ""
            }
            FormSetup.RebootAndRunTask(RegionUUID, Obj)
            Application.DoEvents()
        Next
    End Sub

    Public Sub Backupper(RegionUUID As String)

        ConsoleCommand(RegionUUID, "change region " & """" & Region_Name(RegionUUID) & """", True)
        ' 5 minutes to save an OAR
        'RPC_Save_OAR(RegionUUID, BackupPath() & "/" & Region_Name(RegionUUID) & "_" &
        'DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss", Globalization.CultureInfo.InvariantCulture) & ".oar",
        'Region_Name(RegionUUID), 5 * 60 * 1000)

        ConsoleCommand(RegionUUID, "save oar " & """" & BackupPath() & "/" & Region_Name(RegionUUID) & "_" &
         DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss", Globalization.CultureInfo.InvariantCulture) & ".oar" & """")

    End Sub

#End Region

End Module
