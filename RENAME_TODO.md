# Checklista för namnbyte

## Beslutat namn

Programmet läser musikdata ur PSARC- och Feedpak-arkiv och exporterar bland
annat tabulatur, MIDI, LRC och heatmap. PSARC i namnet ger en tydlig koppling
till den ursprungliga huvudfunktionen, men beskriver inte längre alla
inmatningsformat.

- **Produktnamn:** PsarcKit
- **CLI-kommando:** `psarckit`
- Namnet behåller kopplingen till PSARC och signalerar ett verktyg med flera
  funktioner. Det är kort och lätt att skriva. Programmet stöder även Feedpak;
  dokumentationen ska fortsätta ange formatstödet tydligt.

Kontrollera innan beslut att namnet inte krockar med befintliga projekt,
domännamn eller paket. Välj ett stabilt produktnamn och ett CLI-kommando som
är enkelt att skriva; de behöver inte nödvändigtvis vara identiska.

## Föreslagen ordning

- [x] 1. Välj namn: PsarcKit. Besluta vid genomförandet om gamla kommandon,
  namnrymder eller paketnamn ska finnas kvar som alias under en övergång.
- [ ] 2. Inventera alla gamla namn och identifierare: `rg -n -i 'psarc2tab|Psarc2Tab'`
  inklusive källkod, projektfiler, tester, dokumentation och byggskript.
- [ ] 3. Byt GitHub-repo-namn i samordning med repoägaren. Kontrollera redirects,
  kloner, remotes, länkar, badges, issue-/PR-länkar och externa integrationer.
- [ ] 4. Byt solution-, projekt- och testprojektens namn samt fil-/katalognamn.
  Uppdatera alla project references, solution-poster och CI-sökvägar.
- [ ] 5. Byt kodens namespace, typer och interna produktidentifierare. Se över
  assembly names, root namespace, package ID och eventuella metadata-ID:n.
- [ ] 6. Byt det faktiska CLI-kommandot/binärnamnet för alla publicerade
  plattformar. Kontrollera executable name, apphost, publish-artifacts,
  self-contained paket och eventuella paket-/installationsskript.
- [ ] 7. Uppdatera CLI-hjälp, versionsutskrift, felmeddelanden, miljövariabler
  (till exempel `PSARC2TAB_VGMSTREAM`), README, dokumentation och exempel.
- [ ] 8. Uppdatera GitHub Actions, release-workflow, namn på byggartefakter,
  release-texter, checksumfiler, badges och README-länkar.
- [ ] 9. Bestäm releasehanteringen: låt gamla taggar och releaser ligga kvar;
  använd den nya namngivningen framåt. Lägg vid behov till en övergångsanvisning
  eller wrapper för det gamla kommandot.
- [ ] 10. Uppdatera installationsanvisningar för varje operativsystem, paketering,
  automatisering, exempelskript och eventuella namn på användarinställningar/sökvägar.
- [ ] 11. Kontrollera licens- och upphovsrättstexter, tredjepartsmeddelanden,
  interna dokumentationslänkar och projektbeskrivningen på GitHub.
- [ ] 12. Sök efter de gamla namnen i hela repot igen. Bekräfta att eventuella
  kvarvarande träffar är avsiktliga, till exempel i migreringsanvisningar eller
  äldre releasetaggar.
- [ ] 13. Bygg och kör relevanta tester, granska publicerbara paket för alla
  stödda operativsystem och prova det nya kommandot i en ren miljö. Uppdatera
  changelog och gör namnbytet enligt branchrutinen.

## Att tänka på

- Gamla Git-taggar, releaser och nedladdningslänkar kan finnas i externa
  anvisningar; ändra eller flytta dem inte utan att kontrollera följderna.
- GitHubs omdirigering hjälper för repo och Git-remotes, men kontrollera även
  workflow-token, hemligheter, webhooks och tredjepartstjänster.
- Om binärnamnet ändras kan användarnas skript och PATH-installationer sluta
  fungera. Bestäm om det gamla kommandot ska finnas kvar som alias en tid.
- Namnbytet ändrar inte formatstödet. PSARC, Rocksmith och Feedpak ska fortsatt
  beskrivas som format eller ekosystem, oavsett produktnamn.
