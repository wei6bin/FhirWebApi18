@Environment:Integration
Feature: FhirEngine

Background:
    Given a new HttpClient as unauthenticatedHttpClient
        | HeaderName               | Value   |
    And a new HttpClient as authenticatedHttpClient
        | HeaderName | Value   |
        | x-api-key  | testapp |
  
@HttpClient:unauthenticatedHttpClient
Scenario: Reading a nonexistent resource from an unauthenticated client returns 401 status code
    When getting Appointment/{newguid} as readAppt
    Then readAppt is a Fhir OperationOutcome with data
        | Path       | Value |
        | statusCode | 401   |

@HttpClient:authenticatedHttpClient
Scenario: Reading a nonexistent resource from an authenticated client returns 404 status code
    When getting Appointment/{newguid} as readAppt
    Then readAppt is a Fhir OperationOutcome with data
        | Path       | Value |
        | statusCode | 404   |

@HttpClient:authenticatedHttpClient
Scenario: Metadata endpoint returns the CapabilityStatement with valid canonical links
    When getting metadata as metadata
    Then metadata is a Fhir CapabilityStatement with resolvable links

@HttpClient:unauthenticatedHttpClient   
Scenario: Metadata endpoint is accessible without authentication
    When getting metadata as metadata
    Then metadata has statusCode 200
 
@HttpClient:authenticatedHttpClient   
Scenario: Health endpoint returns 200 status code
    When getting /health?_pretty=true as health
    Then health has statusCode 200


@HttpClient:unauthenticatedHttpClient
Scenario: Swagger endpoint returns 200 status code
    When getting /swagger/v1/swagger.json as openapi
    Then openapi has statusCode 200



@HttpClient:unauthenticatedHttpClient
Scenario: Prometheus scraping endpoint returns 200 status code
    When getting /metrics as metrics
    Then metrics has statusCode 200

