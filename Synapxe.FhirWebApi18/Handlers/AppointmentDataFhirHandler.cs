// -------------------------------------------------------------------------------------------------
// Copyright (c) Integrated Health Information Systems Pte Ltd. All rights reserved.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Hl7.Fhir.Model.CdsHooks;
using Ihis.FhirEngine.Core;
using Ihis.FhirEngine.Core.Data;
using Ihis.FhirEngine.Core.Exceptions;
using Ihis.FhirEngine.Core.Models;
using Ihis.FhirEngine.Core.Search;
using Task = System.Threading.Tasks.Task;

namespace Synapxe.FhirWebApi18.Handlers
{
    [FhirHandlerClass(AcceptedType = nameof(Appointment))]
    public class AppointmentDataFhirHandler
    {
        private readonly IDataService dataStore;
        private readonly ISearchService searchService;

        public AppointmentDataFhirHandler(
            IDataService<Appointment> dataStore,
            ISearchService<Appointment> searchService)
        {
            this.dataStore = dataStore;
            this.searchService = searchService;
        }

        [FhirHandler(FhirInteractionType.OperationInstance, CustomOperation = "cancel")]
        public async Task<Appointment?> CancelAsync(
            IFhirContext context,
            ResourceKey resourceKey,
            CodeableConcept? cancellationReason = default,
            CancellationToken cancellationToken = default)
        {
            var appointment = await dataStore.GetAsync<Appointment>(resourceKey, cancellationToken);

            if (appointment != null)
            {
                appointment.CancelationReason = cancellationReason;
                appointment.Status = Appointment.AppointmentStatus.Cancelled;
                appointment.Meta.LastUpdated = DateTimeOffset.UtcNow;
                await dataStore.UpsertAsync(appointment, WeakETag.FromVersionId(appointment.VersionId), false, true, false, cancellationToken);
                return appointment;
            }
            else
            {
                context.AddOperationOutcomeErrorResponse(new ResourceNotFoundException(resourceKey));
                return null!;
            }
        }

        [FhirHandler("ValidateNoAppointmentConflictOnCreate", HandlerCategory.PreCRUD, FhirInteractionType.Create)]
        public async Task ValidateNoAppointmentConflictAsync(IFhirContext context, Appointment appointment, CancellationToken cancellationToken)
        {
            // Check that all participants have no other appointments for the same time
            var participantIds = appointment.Participant.Select(x => x.Actor.Reference).ToList();

            var searchParams = new List<(string, string)>
            {
                ("_summary", "count"), // we fetch count only
                ("date", appointment.StartElement.ToString()!),
                ("actor", string.Join(',', participantIds)),
            };

            var result = await searchService.SearchAsync("Appointment", searchParams.ToArray(), false, cancellationToken);

            if (result.TotalCount > 0)
            {
                context.AddOperationOutcomeErrorResponse(new ResourceNotValidException("Appointment participant has another appointment for that date."));
            }
        }
    }
}
