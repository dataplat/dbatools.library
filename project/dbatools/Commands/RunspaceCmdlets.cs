using Dataplat.Dbatools.Maintenance;
using Dataplat.Dbatools.Runspace;
using Dataplat.Dbatools.Utility;
using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Implements Get-DbaRunspace. Returns registered runspace containers matching the name filter.
    /// </summary>
    [Cmdlet("Get", "DbaRunspace")]
    [OutputType(typeof(RunspaceContainer))]
    public class GetDbaRunspaceCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Wildcard name filter for runspaces to retrieve.
        /// </summary>
        [Parameter(Position = 0)]
        public string Name { get; set; } = "*";

        /// <inheritdoc />
        protected override void ProcessRecord()
        {
            foreach (RunspaceContainer container in RunspaceHost.Runspaces.Values)
            {
                if (UtilityHost.IsLike(container.Name, Name))
                    WriteObject(container);
            }
        }
    }

    /// <summary>
    /// Implements Register-DbaRunspace. Registers a scriptblock to execute in a managed runspace.
    /// </summary>
    [Cmdlet("Register", "DbaRunspace")]
    public class RegisterDbaRunspaceCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// The scriptblock to execute in the managed runspace.
        /// </summary>
        [Parameter(Mandatory = true)]
        public ScriptBlock ScriptBlock { get; set; }

        /// <summary>
        /// Name to register the runspace under.
        /// </summary>
        [Parameter(Mandatory = true)]
        public string Name { get; set; }

        /// <inheritdoc />
        protected override void ProcessRecord()
        {
            string normalizedName = Name.ToLowerInvariant();

            if (RunspaceHost.Runspaces.ContainsKey(normalizedName))
            {
                WriteMessageVerbose(String.Format("Updating existing runspace: {0}", normalizedName));
                RunspaceHost.Runspaces[normalizedName].SetScript(ScriptBlock);
            }
            else
            {
                WriteMessageVerbose(String.Format("Registering new runspace: {0}", normalizedName));
                RunspaceHost.Runspaces[normalizedName] = new RunspaceContainer(normalizedName, ScriptBlock);
            }
        }
    }

    /// <summary>
    /// Implements Start-DbaRunspace. Starts one or more registered runspaces.
    /// </summary>
    [Cmdlet("Start", "DbaRunspace")]
    public class StartDbaRunspaceCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Names of the runspaces to start.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public string[] Name { get; set; }

        /// <summary>
        /// Runspace containers to start.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public RunspaceContainer[] Runspace { get; set; }

        /// <inheritdoc />
        protected override void ProcessRecord()
        {
            List<RunspaceContainer> toStart = new List<RunspaceContainer>();

            if (Name != null)
            {
                foreach (string name in Name)
                {
                    if (!RunspaceHost.Runspaces.ContainsKey(name.ToLowerInvariant()))
                    {
                        StopFunction(
                            String.Format("Runspace '{0}' is not registered", name),
                            target: name
                        );
                        continue;
                    }
                    toStart.Add(RunspaceHost.Runspaces[name.ToLowerInvariant()]);
                }
            }

            if (Runspace != null)
            {
                foreach (RunspaceContainer rs in Runspace)
                    toStart.Add(rs);
            }

            foreach (RunspaceContainer rs in toStart)
            {
                try
                {
                    WriteMessageVerbose(String.Format("Starting runspace: {0}", rs.Name));
                    rs.Start();
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to start runspace '{0}'", rs.Name),
                        exception: ex,
                        target: rs
                    );
                }
            }
        }
    }

    /// <summary>
    /// Implements Stop-DbaRunspace. Stops one or more running runspaces.
    /// </summary>
    [Cmdlet("Stop", "DbaRunspace")]
    public class StopDbaRunspaceCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Names of the runspaces to stop.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public string[] Name { get; set; }

        /// <summary>
        /// Runspace containers to stop.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public RunspaceContainer[] Runspace { get; set; }

        /// <inheritdoc />
        protected override void ProcessRecord()
        {
            List<RunspaceContainer> toStop = new List<RunspaceContainer>();

            if (Name != null)
            {
                foreach (string name in Name)
                {
                    if (!RunspaceHost.Runspaces.ContainsKey(name.ToLowerInvariant()))
                    {
                        StopFunction(
                            String.Format("Runspace '{0}' is not registered", name),
                            target: name
                        );
                        continue;
                    }
                    toStop.Add(RunspaceHost.Runspaces[name.ToLowerInvariant()]);
                }
            }

            if (Runspace != null)
            {
                foreach (RunspaceContainer rs in Runspace)
                    toStop.Add(rs);
            }

            foreach (RunspaceContainer rs in toStop)
            {
                try
                {
                    WriteMessageVerbose(String.Format("Stopping runspace: {0}", rs.Name));
                    rs.Stop();
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to stop runspace '{0}'", rs.Name),
                        exception: ex,
                        target: rs
                    );
                }
            }
        }
    }

    /// <summary>
    /// Implements Register-DbaMaintenanceTask. Registers a background maintenance task.
    /// </summary>
    [Cmdlet("Register", "DbaMaintenanceTask", DefaultParameterSetName = "Repeating")]
    public class RegisterDbaMaintenanceTaskCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Name of the maintenance task to register.
        /// </summary>
        [Parameter(Mandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// The scriptblock to execute as the maintenance task.
        /// </summary>
        [Parameter(Mandatory = true)]
        public ScriptBlock ScriptBlock { get; set; }

        /// <summary>
        /// Whether to execute the task only once.
        /// </summary>
        [Parameter(ParameterSetName = "Once", Mandatory = true)]
        public SwitchParameter Once { get; set; }

        /// <summary>
        /// Interval between repeated executions of the task.
        /// </summary>
        [Parameter(ParameterSetName = "Repeating", Mandatory = true)]
        public TimeSpan Interval { get; set; }

        /// <summary>
        /// Initial delay before first execution.
        /// </summary>
        [Parameter()]
        public TimeSpan Delay { get; set; }

        /// <summary>
        /// Priority of the maintenance task.
        /// </summary>
        [Parameter()]
        public MaintenancePriority Priority { get; set; } = MaintenancePriority.Medium;

        /// <inheritdoc />
        protected override void ProcessRecord()
        {
            string normalizedName = Name.ToLowerInvariant();

            MaintenanceTask task;

            if (MaintenanceHost.Tasks.ContainsKey(normalizedName))
            {
                task = MaintenanceHost.Tasks[normalizedName];
                task.ScriptBlock = ScriptBlock;
                if (TestBound("Once"))
                    task.Once = Once.ToBool();
                if (TestBound("Interval"))
                    task.Interval = Interval;
                if (TestBound("Delay"))
                    task.Delay = Delay;
                if (TestBound("Priority"))
                    task.Priority = Priority;
            }
            else
            {
                task = new MaintenanceTask();
                task.Name = normalizedName;
                task.ScriptBlock = ScriptBlock;
                task.Once = Once.ToBool();
                task.Interval = Interval;
                task.Delay = Delay;
                task.Priority = Priority;
                task.Registered = DateTime.Now;
                MaintenanceHost.Tasks[normalizedName] = task;
            }
        }
    }
}
