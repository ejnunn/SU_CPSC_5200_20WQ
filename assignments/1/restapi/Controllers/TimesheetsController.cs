using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using restapi.Models;

namespace restapi.Controllers
{
    [Route("[controller]")]
    public class TimesheetsController : Controller
    {
        private readonly TimesheetsRepository repository;

        private readonly ILogger logger;

        public TimesheetsController(ILogger<TimesheetsController> logger)
        {
            repository = new TimesheetsRepository();
            this.logger = logger;
        }

        [HttpGet]
        [Produces(ContentTypes.Timesheets)]
        [ProducesResponseType(typeof(IEnumerable<Timecard>), 200)]
        public IEnumerable<Timecard> GetAll()
        {
            return repository
                .All
                .OrderBy(t => t.Opened);
        }

        [HttpGet("{id:guid}")]
        [Produces(ContentTypes.Timesheet)]
        [ProducesResponseType(typeof(Timecard), 200)]
        [ProducesResponseType(404)]
        public IActionResult GetOne(Guid id)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                return Ok(timecard);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost]
        [Produces(ContentTypes.Timesheet)]
        [ProducesResponseType(typeof(Timecard), 200)]
        public Timecard Create([FromBody] DocumentPerson person)
        {
            logger.LogInformation($"Creating timesheet for {person.ToString()}");

            var timecard = new Timecard(person.Id);

            var entered = new Entered() { Person = person.Id };

            timecard.Transitions.Add(new Transition(entered));

            repository.Add(timecard);

            return timecard;
        }

        /**
         *  Transition a timecard to a deleted state. This is a terminal state and cannot be undone.
         *  This design choice allows for permenant storage of timesheet data, instead of allowing
         *  a client to erase the timesheets from the database entirely.
         */
        [HttpDelete("{id:guid}/deletion")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public IActionResult Delete(Guid id, [FromBody] Deletion deletion)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard == null)
            {
                return NotFound();
            }

            if (timecard.CanBeDeleted() == false)
            {
                return StatusCode(409, new InvalidStateError() { });
            }

            var transition = new Transition(deletion, TimecardStatus.Deleted);

            logger.LogInformation($"Deleting the timesheet {transition}");

            timecard.Transitions.Add(transition);

            repository.Delete(id);

            return Ok(timecard);
        }

        [HttpGet("{id:guid}/lines")]
        [Produces(ContentTypes.TimesheetLines)]
        [ProducesResponseType(typeof(IEnumerable<TimecardLine>), 200)]
        [ProducesResponseType(404)]
        public IActionResult GetLines(Guid id)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                var lines = timecard.Lines
                    .OrderBy(l => l.WorkDate)
                    .ThenBy(l => l.Recorded);

                return Ok(lines);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost("{id:guid}/lines")]
        [Produces(ContentTypes.TimesheetLine)]
        [ProducesResponseType(typeof(TimecardLine), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(InvalidStateError), 409)]
        public IActionResult AddLine(Guid id, [FromBody] DocumentLine documentLine)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status != TimecardStatus.Draft)
                {
                    return StatusCode(409, new InvalidStateError() { });
                }

                var annotatedLine = timecard.AddLine(documentLine);

                repository.Update(timecard);

                return Ok(annotatedLine);
            }
            else
            {
                return NotFound();
            }
        }
        /**
         *  Replaces a timesheet line by deleting the previous timesheet and creating
         *  a new one, with a new GUID.
         */
        [HttpPost("{timesheetId:guid}/lines/{lineId:guid}/replace")]
        [Produces(ContentTypes.TimesheetLine)]
        [ProducesResponseType(typeof(TimecardLine), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(InvalidStateError), 409)]
        [ProducesResponseType(typeof(LineNotFoundError), 104)]
        public IActionResult ReplaceLine(Guid timesheetId, Guid lineId, [FromBody] DocumentLine documentLine)
        {
            logger.LogInformation($"Looking for timesheet {timesheetId} with line {lineId}");

            Timecard timecard = repository.Find(timesheetId);

            if (timecard != null)
            {
                if (timecard.Status != TimecardStatus.Draft)
                {
                    return StatusCode(409, new InvalidStateError() { });
                }

                if (!timecard.HasLine(lineId))
                {
                    return StatusCode(409, new LineNotFoundError() { });
                }

                timecard.deleteLine(lineId);

                var annotatedLine = timecard.AddLine(documentLine);

                repository.Update(timecard);

                return Ok(annotatedLine);
            }
            else
            {
                return NotFound();
            }
        }

        /**
         * Updates an existing timesheet line, but maintains the GUID from the original line.
         */
        [HttpPatch("{timesheetId:guid}/lines/{lineId:guid}/update")]
        [Produces(ContentTypes.TimesheetLine)]
        [ProducesResponseType(typeof(TimecardLine), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(InvalidStateError), 409)]
        [ProducesResponseType(typeof(LineNotFoundError), 104)]
        public IActionResult UpdateLine(Guid timesheetId, Guid lineId, [FromBody] DocumentLine documentLine)
        {
            logger.LogInformation($"Looking for timesheet {timesheetId} with line {lineId}");

            Timecard timecard = repository.Find(timesheetId);

            if (timecard != null)
            {
                if (timecard.Status != TimecardStatus.Draft)
                {
                    return StatusCode(409, new InvalidStateError() { });
                }

                if (!timecard.HasLine(lineId))
                {
                    return StatusCode(409, new LineNotFoundError() { });
                }

                var updatedLine = timecard.updateLine(lineId, documentLine);

                repository.Update(timecard);

                return Ok(updatedLine);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("{id:guid}/transitions")]
        [Produces(ContentTypes.Transitions)]
        [ProducesResponseType(typeof(IEnumerable<Transition>), 200)]
        [ProducesResponseType(404)]
        public IActionResult GetTransitions(Guid id)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                return Ok(timecard.Transitions);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost("{id:guid}/submittal")]
        [Produces(ContentTypes.Transition)]
        [ProducesResponseType(typeof(Transition), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(InvalidStateError), 409)]
        [ProducesResponseType(typeof(EmptyTimecardError), 409)]
        public IActionResult Submit(Guid id, [FromBody] Submittal submittal)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status != TimecardStatus.Draft)
                {
                    return StatusCode(409, new InvalidStateError() { });
                }

                if (timecard.Lines.Count < 1)
                {
                    return StatusCode(409, new EmptyTimecardError() { });
                }

                var transition = new Transition(submittal, TimecardStatus.Submitted);

                logger.LogInformation($"Adding submittal {transition}");

                timecard.Transitions.Add(transition);

                repository.Update(timecard);

                return Ok(transition);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("{id:guid}/submittal")]
        [Produces(ContentTypes.Transition)]
        [ProducesResponseType(typeof(Transition), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(MissingTransitionError), 409)]
        public IActionResult GetSubmittal(Guid id)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status == TimecardStatus.Submitted)
                {
                    var transition = timecard.Transitions
                                        .Where(t => t.TransitionedTo == TimecardStatus.Submitted)
                                        .OrderByDescending(t => t.OccurredAt)
                                        .FirstOrDefault();

                    return Ok(transition);
                }
                else
                {
                    return StatusCode(409, new MissingTransitionError() { });
                }
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost("{id:guid}/returned")]
        [Produces(ContentTypes.Transition)]
        [ProducesResponseType(typeof(Transition), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(InvalidStateError), 409)]
        public IActionResult Return(Guid id, [FromBody] Returned returner)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status != TimecardStatus.Submitted)
                {
                    return StatusCode(409, new InvalidStateError() { });
                }

                var transition = new Transition(returner, TimecardStatus.Draft);

                logger.LogInformation($"Adding submittal {transition}");

                timecard.Transitions.Add(transition);

                repository.Update(timecard);

                return Ok(transition);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost("{id:guid}/cancellation")]
        [Produces(ContentTypes.Transition)]
        [ProducesResponseType(typeof(Transition), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(InvalidStateError), 409)]
        [ProducesResponseType(typeof(EmptyTimecardError), 409)]
        public IActionResult Cancel(Guid id, [FromBody] Cancellation cancellation)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status != TimecardStatus.Draft && timecard.Status != TimecardStatus.Submitted)
                {
                    return StatusCode(409, new InvalidStateError() { });
                }

                var transition = new Transition(cancellation, TimecardStatus.Cancelled);

                logger.LogInformation($"Adding cancellation transition {transition}");

                timecard.Transitions.Add(transition);

                repository.Update(timecard);

                return Ok(transition);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("{id:guid}/cancellation")]
        [Produces(ContentTypes.Transition)]
        [ProducesResponseType(typeof(Transition), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(MissingTransitionError), 409)]
        public IActionResult GetCancellation(Guid id)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status == TimecardStatus.Cancelled)
                {
                    var transition = timecard.Transitions
                                        .Where(t => t.TransitionedTo == TimecardStatus.Cancelled)
                                        .OrderByDescending(t => t.OccurredAt)
                                        .FirstOrDefault();

                    return Ok(transition);
                }
                else
                {
                    return StatusCode(409, new MissingTransitionError() { });
                }
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost("{id:guid}/rejection")]
        [Produces(ContentTypes.Transition)]
        [ProducesResponseType(typeof(Transition), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(InvalidStateError), 409)]
        [ProducesResponseType(typeof(EmptyTimecardError), 409)]
        public IActionResult Reject(Guid id, [FromBody] Rejection rejection)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status != TimecardStatus.Submitted)
                {
                    return StatusCode(409, new InvalidStateError() { });
                }

                var transition = new Transition(rejection, TimecardStatus.Rejected);

                logger.LogInformation($"Adding rejection transition {transition}");

                timecard.Transitions.Add(transition);

                repository.Update(timecard);

                return Ok(transition);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("{id:guid}/rejection")]
        [Produces(ContentTypes.Transition)]
        [ProducesResponseType(typeof(Transition), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(MissingTransitionError), 409)]
        public IActionResult GetRejection(Guid id)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status == TimecardStatus.Rejected)
                {
                    var transition = timecard.Transitions
                                        .Where(t => t.TransitionedTo == TimecardStatus.Rejected)
                                        .OrderByDescending(t => t.OccurredAt)
                                        .FirstOrDefault();

                    return Ok(transition);
                }
                else
                {
                    return StatusCode(409, new MissingTransitionError() { });
                }
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost("{id:guid}/approval")]
        [Produces(ContentTypes.Transition)]
        [ProducesResponseType(typeof(Transition), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(InvalidStateError), 409)]
        [ProducesResponseType(typeof(EmptyTimecardError), 409)]
        public IActionResult Approve(Guid id, [FromBody] Approval approval)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status != TimecardStatus.Submitted)
                {
                    return StatusCode(409, new InvalidStateError() { });
                }

                // The timecard person cannot approve their own timecard
                if (timecard.Employee == approval.Person)
                {
                    return StatusCode(409, new InvalidSubmitterError() { });
                }

                var transition = new Transition(approval, TimecardStatus.Approved);

                logger.LogInformation($"Adding approval transition {transition}");

                timecard.Transitions.Add(transition);

                repository.Update(timecard);

                return Ok(transition);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("{id:guid}/approval")]
        [Produces(ContentTypes.Transition)]
        [ProducesResponseType(typeof(Transition), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(typeof(MissingTransitionError), 409)]
        public IActionResult GetApproval(Guid id)
        {
            logger.LogInformation($"Looking for timesheet {id}");

            Timecard timecard = repository.Find(id);

            if (timecard != null)
            {
                if (timecard.Status == TimecardStatus.Approved)
                {
                    var transition = timecard.Transitions
                                        .Where(t => t.TransitionedTo == TimecardStatus.Approved)
                                        .OrderByDescending(t => t.OccurredAt)
                                        .FirstOrDefault();

                    return Ok(transition);
                }
                else
                {
                    return StatusCode(409, new MissingTransitionError() { });
                }
            }
            else
            {
                return NotFound();
            }
        }
    }
}
