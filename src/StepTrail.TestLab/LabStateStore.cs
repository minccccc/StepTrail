using System.Collections.Concurrent;

namespace StepTrail.TestLab;

public sealed class LabStateStore
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<LabRequestRecord> _requests = new();

    private string _activeScenario = LabScenarioNames.HappyPath;
    private int _apiACallCount;
    private int _apiBCallCount;
    private bool _demoWorkflowReady;
    private string? _demoWorkflowStatus;
    private Guid? _lastWorkflowInstanceId;
    private string? _lastTriggerSummary;

    public string ActivateScenario(string? scenarioName)
    {
        var normalized = LabScenarioNames.Normalize(scenarioName);

        lock (_gate)
        {
            _activeScenario = normalized;
            _apiACallCount = 0;
            _apiBCallCount = 0;
            _lastWorkflowInstanceId = null;
            _lastTriggerSummary = $"Scenario switched to '{normalized}'.";

            while (_requests.TryDequeue(out _))
            {
            }
        }

        return normalized;
    }

    public void ResetActivity()
    {
        lock (_gate)
        {
            _apiACallCount = 0;
            _apiBCallCount = 0;
            _lastWorkflowInstanceId = null;
            _lastTriggerSummary = "Request log and counters were reset.";

            while (_requests.TryDequeue(out _))
            {
            }
        }
    }

    public int NextApiACall()
    {
        lock (_gate)
        {
            _apiACallCount++;
            return _apiACallCount;
        }
    }

    public int NextApiBCall()
    {
        lock (_gate)
        {
            _apiBCallCount++;
            return _apiBCallCount;
        }
    }

    public string GetActiveScenario()
    {
        lock (_gate)
        {
            return _activeScenario;
        }
    }

    public void RecordRequest(LabRequestRecord record)
    {
        _requests.Enqueue(record);

        while (_requests.Count > 40 && _requests.TryDequeue(out _))
        {
        }
    }

    public void MarkDemoWorkflowReady(string status)
    {
        lock (_gate)
        {
            _demoWorkflowReady = true;
            _demoWorkflowStatus = status;
        }
    }

    public void NoteTrigger(Guid? instanceId, string summary)
    {
        lock (_gate)
        {
            _lastWorkflowInstanceId = instanceId;
            _lastTriggerSummary = summary;
        }
    }

    public LabSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new LabSnapshot
            {
                ActiveScenario = _activeScenario,
                ApiACallCount = _apiACallCount,
                ApiBCallCount = _apiBCallCount,
                DemoWorkflowReady = _demoWorkflowReady,
                DemoWorkflowStatus = _demoWorkflowStatus,
                LastWorkflowInstanceId = _lastWorkflowInstanceId,
                LastTriggerSummary = _lastTriggerSummary,
                Requests = _requests.ToArray()
                    .OrderByDescending(record => record.ReceivedAtUtc)
                    .ToArray()
            };
        }
    }
}
