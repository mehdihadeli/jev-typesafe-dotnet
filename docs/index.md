---
layout: home
title: TypeSafe .NET SDK
titleTemplate: Typed decisions for .NET
description: Ask structured questions about text or JSON state and use Jev answers directly in .NET code.
hero:
  name: TypeSafe .NET SDK
  text: Typed decisions for .NET
  tagline: Send state and typed questions to Jev, then branch on choices, scores, probabilities, and confidence without parsing generated text.
  actions:
    - theme: brand
      text: Start building
      link: /guide/quickstart
    - theme: alt
      text: Understand the architecture
      link: /guide/architecture
features:
  - icon: "01"
    title: Ask atomic questions
    details: Mix Noul, Choice, and Score questions in one request against the same state.
  - icon: "02"
    title: Keep decisions structured
    details: Deserialize answers into typed C# models with probabilities and confidence available to application code.
  - icon: "03"
    title: Test the boundary
    details: Use isolated unit tests for HTTP contracts and opt-in integration tests for the live API.
---

<div class="home-note">
  <strong>Core promise:</strong> Jev produces structured judgments that software can consume directly.
</div>

## Choose your next step

<div class="home-paths">
  <a href="/guide/quickstart" class="home-path">
    <strong>Make your first request</strong>
    <span>Install the package, configure an API key, and ask mixed question types.</span>
  </a>
  <a href="/guide/architecture" class="home-path">
    <strong>Understand the pipeline</strong>
    <span>See how request models, transport, converters, and typed responses fit together.</span>
  </a>
  <a href="/reference/api" class="home-path">
    <strong>Find an API type</strong>
    <span>Browse the client methods, question primitives, answer models, and errors.</span>
  </a>
</div>

## Built for application decisions

Jev is TypeSafe's System One model. It evaluates typed questions independently against shared text or structured state. The SDK keeps that boundary explicit: your application chooses the questions and thresholds; Jev returns the evidence and probabilities.

<div class="home-grid">
  <a href="/reference/api" class="home-grid-item">
    <span class="home-grid-kicker">PRIMITIVES</span>
    <strong>Noul, Choice, Score</strong>
    <span>Use a yes/no probability, a named alternative, or an ordered rubric.</span>
  </a>
  <a href="/guide/workflow" class="home-grid-item">
    <span class="home-grid-kicker">TESTING</span>
    <strong>Unit plus live checks</strong>
    <span>Mock the HTTP boundary by default and opt into authenticated integration tests.</span>
  </a>
  <a href="/guide/development" class="home-grid-item">
    <span class="home-grid-kicker">LIBRARY</span>
    <strong>Small .NET surface</strong>
    <span>Use HttpClient, System.Text.Json, cancellation tokens, and standard dependency injection patterns.</span>
  </a>
</div>
