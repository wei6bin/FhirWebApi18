@Environment:Integration
Feature: Appointment

Background:
    Given a random tag
    And a new HttpClient as httpClient
        | HeaderName | Value   |
        | x-api-key  | testapp |

Scenario: Reading a newly created appointment returns exactly the same appointment
    Given a Resource is created from Samples/Appointment.json as createdAppt
    When getting Appointment/{createdAppt.Id} as readAppt
    Then createdAppt is a Fhir Appointment with data
        | Path       | Value |
        | statusCode | 201   |
    And createdAppt and readAppt are exactly the same

Scenario Outline: Create an appointment with a start date in the past returns 422 status code
    Given a Resource is created from Samples/Appointment.json with data as createdResponse
        | Path  | Value                     | FhirType |
        | start | <startDateTime>           | instant  |
    Then createdResponse is a Fhir OperationOutcome with data
        | Path                  | Value                                                                                                      | FhirType |
        | statusCode            | 422                                                                                                        |          |
        | issue[0].severity     | error                                                                                                      | code     |
        | issue[0].details.text | Instance failed constraint ihis-apt-1 "If present, start SHALL have a higher value than current date time" | string   |

Examples:
    | startDateTime             |
    | 2000-01-01T12:00:00+08:00 |
    | 1985-01-01T12:00:00+08:00 |

Scenario: Searching an appointment by patient returns the appointment
    Given a Resource is created from Samples/Appointment.json with data as newAppt
        | Path                 | Value           | FhirType  |
        | participant[0].actor | Patient/{#pat1} | Reference |
    When getting Appointment?patient=Patient/{#pat1} as searchBundle
    Then searchBundle is a Fhir Bundle which contains newAppt

Scenario: Searching an appointment by actor returns the appointment with correct sort
    Given a Resource is created from Samples/Appointment.json with data as newAppt1
        | Path                 | Value                     | FhirType  |
        | participant[0].actor | Patient/{#pat2}           | Reference |
        | start                | 2125-01-01T12:00:00+08:00 | instant   |
        | end                  | 2125-01-01T12:00:00+08:00 | instant   |
    And a Resource is created from Samples/Appointment.json with data as newAppt2
        | Path                 | Value                     | FhirType  |
        | participant[0].actor | Patient/{#pat3}           | Reference |
        | start                | 2125-02-01T12:00:00+08:00 | instant   |
        | end                  | 2125-02-01T12:00:00+08:00 | instant   |
    And a Resource is created from Samples/Appointment.json with data as newAppt3
        | Path                 | Value                     | FhirType  |
        | participant[0].actor | Patient/{#pat2}           | Reference |
        | start                | 2125-03-01T12:00:00+08:00 | instant   |
        | end                  | 2125-03-01T12:00:00+08:00 | instant   |
    When getting Appointment?actor=Patient/{#pat2}&_sort=_lastUpdated as searchBundle
    Then searchBundle is a Fhir Bundle which contains newAppt1,newAppt3
   
Scenario: Searching an appointment by date returns the appointment
    Given a Resource is created from Samples/Appointment.json with data as newAppt
        | Path  | Value                     | FhirType |
        | start | 2122-01-01T12:00:00+08:00 | instant  |
        | end   | 2122-01-01T12:00:00+08:00 | instant  |
    When getting Appointment?date=2122-01-01&_tag={currentTag} as searchBundle
    Then searchBundle is a Fhir Bundle which contains newAppt



Scenario: Moving an appointment returns 200
    Given a Resource is created from Samples/Appointment.json as createdAppt
    When executing operation Appointment/{createdAppt.Id}/$move-appointment-date with data as updatedAppt
        | Name              | Value     | FhirType        |
        | days-in-future    | 5         | integer         |
    Then updatedAppt is a Fhir Appointment with data
        | Path       | Value | FhirType |
        | statusCode | 200   |          |


Scenario: Updating a newly created appointment with an invalid cancellation reason returns 422
    Given a Resource is created from Samples/Appointment.json as createdAppt
    When updating Appointment/{createdAppt.Id} with data as updatedAppt
        | Path              | Value     | FhirType        |
        | status            | cancelled | code            |
        | cancelationReason | xyz       | CodeableConcept |
    Then updatedAppt is a Fhir OperationOutcome with data
        | Path                  | Value                                                                                                                       | FhirType |
        | statusCode            | 422                                                                                                                         |          |
        | issue[0].severity     | error                                                                                                                       | code     |
        | issue[0].code         | code-invalid                                                                                                                | code     |
        | issue[0].details.text | Code 'xyz' does not exist in the value set 'Care Programs' (http://sypnapxe.sg/fhir/ValueSet/appointmentCancellationReason) | code     |

Scenario: Cancelling a nonexistent appointment returns 404 status code
    When executing operation Appointment/{newguid}/$cancel with data as cancellationResponse
        | Name               | Value         | FhirType        |
        | cancellationReason | lost-interest | CodeableConcept |
    Then cancellationResponse is a Fhir OperationOutcome with data
        | Path       | Value |
        | statusCode | 404   |

Scenario: Cancelling an appointment with an invalid cancellation reason returns 422 status code
    Given a Resource is created from Samples/Appointment.json as createdAppointment
    When executing operation Appointment/{createdAppointment.Id}/$cancel with data as cancellationResponse
        | Name               | Value | FhirType        |
        | cancellationReason | abc   | CodeableConcept |
    Then cancellationResponse is a Fhir OperationOutcome with data
        | Path                  | Value                                                                                                                       | FhirType |
        | statusCode            | 422                                                                                                                         |          |
        | issue[0].severity     | error                                                                                                                       | code     |
        | issue[0].code         | code-invalid                                                                                                                | code     |
        | issue[0].details.text | Code 'abc' does not exist in the value set 'Care Programs' (http://sypnapxe.sg/fhir/ValueSet/appointmentCancellationReason) | code     |



Scenario: Updating a newly created appointment returns an appointment with an incremented versionId
    Given a Resource is created from Samples/Appointment.json as createdAppt
    When updating Appointment/{createdAppt.Id} with data as updatedAppt
        | Path        | Value        | FhirType |
        | description | For followup | string   |
    And getting Appointment/{createdAppt.Id}/_history/2 as readUpdatedAppt
    Then updatedAppt and readUpdatedAppt are exactly the same
    And updatedAppt is a Fhir Appointment with data
        | Path         | Value |
        | statusCode   | 200   |
        | headers.eTag | W/"2" |
    And readUpdatedAppt is a Fhir Appointment with data
        | Path           | Value        | FhirType |
        | description    | For followup | string   |
        | meta.versionId | 2            | string   |

Scenario: Creating an appointment where same participant is in a different appointment with the same schedule returns 422 status code
    Given a Resource is created from Samples/Appointment.json with data as existingAppt
        | Path                  | Value                     | FhirType  |
        | participant[0].actor  | Practitioner/{#prac1}     | Reference |
        | participant[0].status | tentative                 | code      |
        | start                 | {#schedtime(datetime+2d)} | instant   |
    When creating from Samples/Appointment.json with data as createdResponse
        | Path                  | Value                     | FhirType  |
        | participant[0].actor  | Practitioner/{#prac1}     | Reference |
        | participant[0].status | tentative                 | code      |
        | start                 | {#schedtime(datetime+2d)} | instant   |
    Then createdResponse is a Fhir OperationOutcome with data
        | Path                 | Value                                                          | FhirType |
        | statusCode           | 422                                                            |          |
        | issue[0].severity    | error                                                          | code     |
        | issue[0].code        | invalid                                                        | code     |
        | issue[0].diagnostics | Appointment participant has another appointment for that date. | string   |
    And existingAppt is a Fhir Appointment

Scenario: Cancelling an appointment returns appointment with cancelled status and incremented version
    Given a Resource is created from Samples/Appointment.json as createdAppointment
    When executing operation Appointment/{createdAppointment.Id}/$cancel with data as cancellationResponse
        | Name               | Value         | FhirType        |
        | cancellationReason | lost-interest | CodeableConcept |
    And getting Appointment/{cancellationResponse.Id} as readAppt
    Then cancellationResponse is a Fhir Appointment with data
        | Path              | Value         | FhirType        |
        | statusCode        | 200           |                 |
        | meta.versionId    | 2             | string          |
        | status            | cancelled     | code            |
        | cancelationReason | lost-interest | CodeableConcept |
    And cancellationResponse and readAppt are exactly the same

Scenario: Updating an appointment resource with a mismatch of id in the body and path returns 400 status code
    Given a Resource is created from Samples/Appointment.json as createdAppt
    When updating createdAppt with data as updatedAppt
        | Path        | Value        | FhirType |
        | description | For followup | string   |
        | id          | {newguid}    | id       |
    Then updatedAppt is a Fhir OperationOutcome with data
        | Path                 | Value                                                            | FhirType |
        | statusCode           | 400                                                              |          |
        | issue[0].diagnostics | Resource ID in resource does not match with Resource ID in path. | string   |

