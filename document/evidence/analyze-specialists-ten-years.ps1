param([Parameter(Mandatory=$true)][string]$ReportRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ReportRoot).Path
$report=Get-Content (Join-Path $root 'report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if(!$report.Completed -or $report.CompletedTicks -lt $report.RequestedTicks){throw 'Ten-year simulation is incomplete; do not label partial samples as ten-year results.'}
$yearTicks=[long]($report.RequestedTicks/$report.Settings.Years)
$initial=$report.Samples[0]
$last=$report.Samples[-1]
$annual=@($report.Samples | Where-Object { $_.Tick % $yearTicks -eq 0 -or $_.Tick -eq $report.CompletedTicks } | Group-Object Tick | ForEach-Object { $_.Group[-1] })
$csvRows=foreach($sample in $annual){foreach($actor in $sample.Characters){
 [pscustomobject]@{
 Year=$sample.ElapsedYears;Tick=$sample.Tick;Name=$actor.Name;ActorId=$actor.Id;Profession=$actor.Profession
 Stones=$actor.Stones;Organization=$actor.OrganizationId;ServicesCompleted=$actor.ServicesCompleted;ServiceRevenue=$actor.ServiceRevenue
 ServicesRefunded=$actor.ServicesRefunded;LastServiceTick=$actor.LastServiceTick
 Herbs=$actor.Items.herb;Iron=$actor.Items.'iron-material';Hides=$actor.Items.'wolf-hide';Ointment=$actor.Items.ointment;Powder=$actor.Items.'bleeding-powder'
 MedicineXP=$actor.LifeSkills.MedicineExperience;PharmacyXP=$actor.LifeSkills.PharmacyExperience;SmithingXP=$actor.LifeSkills.SmithingExperience
 SpiritLevel=$actor.SpiritLevel;BodyLevel=$actor.BodyLevel;QiLevel=$actor.QiLevel;Awakening=$actor.Awakening
 EarnedTrainingExperience=$actor.EarnedTrainingExperience;LastTrainingTick=$actor.LastTrainingTick
 }
}}
$csvRows | Export-Csv -LiteralPath (Join-Path $root 'annual.csv') -NoTypeInformation -Encoding UTF8
$summary=foreach($actor in $last.Characters){
 $start=$initial.Characters | Where-Object {$_.Id -eq $actor.Id}
 [pscustomobject]@{Name=$actor.Name;ActorId=$actor.Id;InitialStones=$start.Stones;FinalStones=$actor.Stones;StoneChange=$actor.Stones-$start.Stones
 ServiceRevenue=$actor.ServiceRevenue;OtherCashIn=$actor.CashIn-$actor.ServiceRevenue;CashOut=$actor.CashOut;ClientServiceSpending=$actor.ClientServiceSpending
 Services=$actor.ServicesCompleted;LastServiceTick=$actor.LastServiceTick;LastServiceYear=if($actor.LastServiceTick -ge 0){$actor.LastServiceTick/[double]$yearTicks}else{$null}
 FinalOrganization=$actor.OrganizationId;Herbs=$actor.Items.herb;Iron=$actor.Items.'iron-material';Hides=$actor.Items.'wolf-hide'
 MedicineXP=$actor.LifeSkills.MedicineExperience;PharmacyXP=$actor.LifeSkills.PharmacyExperience;SmithingXP=$actor.LifeSkills.SmithingExperience
 SpiritLevel=$actor.SpiritLevel;BodyLevel=$actor.BodyLevel;QiLevel=$actor.QiLevel;Awakening=$actor.Awakening;AwakeningAttempts=$actor.AwakeningAttempts
 TrainingExperience=$actor.EarnedTrainingExperience;LastTrainingTick=$actor.LastTrainingTick}
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding UTF8
$summary | Format-Table Name,InitialStones,FinalStones,StoneChange,Services,ServiceRevenue,SpiritLevel,BodyLevel,QiLevel,TrainingExperience -AutoSize
Write-Output ('CompletedTicks='+$report.CompletedTicks+' Years='+$report.ElapsedYears+' RunSeconds='+$report.RunSeconds+' ArchivedEvents='+$report.ArchivedEvents)
Write-Output ('SectStones='+$last.SectStones+' ExternalTestFunding='+$last.ExternalTestFunding)
