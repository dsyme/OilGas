﻿namespace OilGas.LeakDetector.Core

open Core
open System.Threading
open FSharp.Collections.ParallelSeq
open Microsoft.ServiceBus.Messaging
open EventReceiver
open OilGas.TelemetryCore
open MathNet.Numerics
//Volume flow rate (Q) = volume of fluid flowing past a section per unit time (m3/s)   
//Weight flow rate (W) = weight of fluid flowing past a section per unit time   
//Mass of flow rate (M) = mass of fluid flowing past a section per unit time    
//Q = A*v ; where A is the area and v is the velocity. (m3/s)   
//W = Q*γ; where Q is the flow rate and γ is the specific weight.  (m3/s * N/ m3 = N/s)   
//M = Q*ρ; where Q is the flow rate, and ρ is the density.  ( = kg/ m3 * m3/s = kg/s)  

module LeakJobs =
    open System.Collections

    let sqr a = a*a
    let calculateDistance(p1:vector2, p2:vector2) =
        ((p1.x - p2.x) |> sqr) - ((p1.y - p2.y) |> sqr) |> sqrt
    type AnalyticsLabel =
    | A
    | B
    | C
    | Unknown   
    type FlowWarningLevel =
    | High
    | Medium
    | Low
    | None
    type AnalyticsRegion =
        {label:AnalyticsLabel; gps:vector2} 
    type RegionalEvent =
        {event:PipeFlowTelemetryEvent; region:AnalyticsLabel; massFlow:float32}
    type RegionalWarning =
        {region:AnalyticsLabel;warningLevel:FlowWarningLevel;events:RegionalEvent seq}
    let calculateMassFlow(event:PipeFlowTelemetryEvent) =
        //need to get actual density
        let fluidDensity = 1.0f
        //need to do look up on cross sectional area
        let pipeCrossSectionalArea = 2.3f
        pipeCrossSectionalArea * event.Flow * fluidDensity
    let mapToRegionalEvent(event:PipeFlowTelemetryEvent) =
        //Region GPS locations
        let A = {label = A; gps = {x = 49.899f; y = -97.139f};}
        let B = {label = B; gps = {x = 50.450f; y = -104.600f};}
        let C = {label = C; gps = {x = 35.482f; y = -97.535f};}
        
        let regions =
                [A;B;C]  
                |> Seq.filter(fun region -> 
                        calculateDistance(region.gps, {x=event.Longitude; y=event.Latitude;}) < 100.0f)
                |> Seq.map(fun region -> region.label)
                |> Seq.head
        {event = event; region = regions; massFlow = calculateMassFlow(event)}
    let detectLeak(events:PipeFlowTelemetryEvent seq) =
        events 
        |> PSeq.map(fun event -> event |> mapToRegionalEvent)
        |> PSeq.groupBy(fun event -> event.region)
        |> PSeq.map(fun (key,events) -> 
                    let warningThreshold = 1.0f
                    let totalFlow = events |> Seq.fold(fun acc e -> acc + e.massFlow) 0.0f
                    if (totalFlow / ((events |> Seq.length) |> float32) > warningThreshold)
                    then {region = key; warningLevel = High; events = events}
                    else {region = key; warningLevel = High; events = events} )
    let scheduleLeakJob(events:PipeFlowTelemetryEvent seq) =
        events |> detectLeak
        