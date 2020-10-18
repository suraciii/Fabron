using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;
using TGH.Server.Entities;
using TGH.Server.Services;

namespace TGH.Server.Grains
{

    public interface IJobGrain : IGrainWithGuidKey
    {
        [AlwaysInterleave]
        Task Cancel(string reason);
        [ReadOnly]
        Task<JobState> GetState();
        Task Create(string commandName, string commandRawData);
    }

    public class JobGrain : Grain, IJobGrain, IRemindable
    {
        private readonly ILogger _logger;
        private readonly IPersistentState<JobState> _job;
        private readonly IMediator _mediator;
        private IGrainReminder? reminder;
        private CancellationTokenSource? cancellationTokenSource;

        public JobGrain(
            ILogger<JobGrain> logger,
            [PersistentState("Job", "JobStore")] IPersistentState<JobState> job,
            IMediator mediator)
        {
            _logger = logger;
            _job = job;
            _mediator = mediator;
        }

        public Task<JobState> GetState()
        {
            return Task.FromResult(_job.State);
        }

        public async Task Create(string commandName, string commandData)
        {
            if (!_job.RecordExists)
            {
                _job.State = new JobState(commandName, commandData);
                await _job.WriteStateAsync();
                _logger.LogInformation($"Created Job");
            }

            reminder = await RegisterOrUpdateReminder("Check", TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(20));
            _logger.LogInformation($"Job Reminder Registered");
            _ = Go();
        }

        public async Task Cancel(string reason)
        {
            if (cancellationTokenSource is null)
                throw new InvalidOperationException();

            if (!cancellationTokenSource.IsCancellationRequested)
                cancellationTokenSource.Cancel();

            _job.State.Cancel(reason);
            await _job.WriteStateAsync();
        }

        private async Task Start()
        {
            _logger.LogInformation($"Start Job");
            _job.State.Start();
            await _job.WriteStateAsync();
            _logger.LogInformation($"Job Started");
            await Run();
        }


        private async Task Run()
        {
            _logger.LogInformation($"Run Job");
            cancellationTokenSource ??= new CancellationTokenSource(TimeSpan.FromMinutes(10));
            try
            {

                var result = await _mediator.Handle(_job.State.Command.Name, _job.State.Command.Data, cancellationTokenSource.Token);
                _job.State.Complete(result);
            }
            catch (Exception e)
            {
                if (e is not TaskCanceledException || _job.State.Status != JobStatus.Canceled)
                {
                    _job.State.Fault(e);
                }
            }
            await _job.WriteStateAsync();
            _logger.LogInformation($"Job Finished: {_job.State.Status}");
            await Cleanup();
        }

        private async Task Cleanup()
        {
            _logger.LogInformation($"Cleanup Job");
            if (reminder is null)
                reminder = await GetReminder("Check");
            if (reminder is not null)
            {
                await UnregisterReminder(reminder);
                _logger.LogInformation($"Job Reminder Unregistered");
            }
            DeactivateOnIdle();
        }

        private Task Go() => _job.State.Status switch
        {
            JobStatus.Created => Start(),
            JobStatus.Running => Run(),
            _ => Cleanup()
        };

        Task IRemindable.ReceiveReminder(string reminderName, TickStatus status) => Go();
    }
}
