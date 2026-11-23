# Erzeugt eine SimaticML-Datei fuer OB1 "Main" in FUP (FBD) fuer TIA Portal V20.
param([string]$Out)
$ns = 'http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4'
$script:id = 10
function NextId { $script:id++; return ('{0:X}' -f $script:id) }
function MLText($comp, $text) {
  $a = NextId; $b = NextId
  return "<MultilingualText ID=`"$a`" CompositionName=`"$comp`"><ObjectList><MultilingualTextItem ID=`"$b`" CompositionName=`"Items`"><AttributeList><Culture>de-DE</Culture><Text>$([Security.SecurityElement]::Escape($text))</Text></AttributeList></MultilingualTextItem></ObjectList></MultilingualText>"
}
function G($uid, $name) { "<Access Scope=`"GlobalVariable`" UId=`"$uid`"><Symbol><Component Name=`"$name`" /></Symbol></Access>" }
function GConst($uid, $name) { "<Access Scope=`"GlobalConstant`" UId=`"$uid`"><Constant Name=`"$name`" /></Access>" }
function LC($uid, $type, $val) { "<Access Scope=`"LiteralConstant`" UId=`"$uid`"><Constant><ConstantType>$type</ConstantType><ConstantValue>$val</ConstantValue></Constant></Access>" }
function W($from, $to) { $u = $script:wid; $script:wid++; "<Wire UId=`"$u`">$from$to</Wire>" }
function NC($uid, $name) { "<NameCon UId=`"$uid`" Name=`"$name`" />" }
function EnOpen($uid) { $u = $script:wid; $script:wid++; "<Wire UId=`"$u`"><OpenCon UId=`"$(90 + $u - 100)`" /><NameCon UId=`"$uid`" Name=`"en`" /></Wire>" }
function OutOpen($uid,$name) { $u = $script:wid; $script:wid++; "<Wire UId=`"$u`"><NameCon UId=`"$uid`" Name=`"$name`" /><OpenCon UId=`"$(90 + $u - 100)`" /></Wire>" }
function IC($uid) { "<IdentCon UId=`"$uid`" />" }

function Network($title, $comment, $parts, $wires) {
  $cu = NextId
  $flg = "<FlgNet xmlns=`"$ns`"><Parts>$($parts -join '')</Parts><Wires>$($wires -join '')</Wires></FlgNet>"
  return "<SW.Blocks.CompileUnit ID=`"$cu`" CompositionName=`"CompileUnits`"><AttributeList><NetworkSource>$flg</NetworkSource><ProgrammingLanguage>FBD</ProgrammingLanguage></AttributeList><ObjectList>$(MLText 'Comment' $comment)$(MLText 'Title' $title)</ObjectList></SW.Blocks.CompileUnit>"
}

$nets = @()

# NW1: Freigabe = Nothalt UND Motorschutz
$script:wid = 100
$nets += Network 'Freigabe Sicherheitskreis' 'Freigabe nur bei entriegeltem Not-Halt und intaktem Motorschutz' @(
  (G 21 'S_Nothalt'), (G 22 'M_Motorschutz'), (G 23 'M_Freigabe'),
  '<Part Name="A" UId="24"><TemplateValue Name="Card" Type="Cardinality">2</TemplateValue></Part>',
  '<Part Name="Coil" UId="25" />'
) @(
  (W (IC 21) (NC 24 'in1')), (W (IC 22) (NC 24 'in2')), (W (NC 24 'out') (NC 25 'in')), (W (IC 23) (NC 25 'operand'))
)

# NW2: Antrieb Band 1 (FB_Motor mit Instanz-DB)
$script:wid = 100
$nets += Network 'Antrieb Band 1' 'Motorsteuerung mit Selbsthaltung, Stoerspeicher und Betriebsstundenzaehler' @(
  (G 21 'S_Start'), (G 22 'S_Stop'), (G 23 'M_Freigabe'), (G 24 'M_Motorschutz'), (G 25 'S_Quittieren'),
  (G 26 'Q_Motor'), (G 27 'Q_LampeStoerung'),
  ('<Call UId="30"><CallInfo Name="FB_Motor" BlockType="FB"><Instance Scope="GlobalVariable" UId="31"><Component Name="DB_Motor_Band1" /></Instance>' +
   '<Parameter Name="Start" Section="Input" Type="Bool" /><Parameter Name="Stop" Section="Input" Type="Bool" /><Parameter Name="Freigabe" Section="Input" Type="Bool" />' +
   '<Parameter Name="Motorschutz" Section="Input" Type="Bool" /><Parameter Name="Quittieren" Section="Input" Type="Bool" />' +
   '<Parameter Name="Motor" Section="Output" Type="Bool" /><Parameter Name="Stoerung" Section="Output" Type="Bool" /><Parameter Name="Status" Section="Output" Type="&quot;UDT_MotorStatus&quot;" />' +
   '</CallInfo></Call>')
) @(
  (W (IC 21) (NC 30 'Start')), (W (IC 22) (NC 30 'Stop')), (W (IC 23) (NC 30 'Freigabe')), (W (IC 24) (NC 30 'Motorschutz')), (W (IC 25) (NC 30 'Quittieren')),
  (W (NC 30 'Motor') (IC 26)), (W (NC 30 'Stoerung') (IC 27))
)

# NW3: Stueckzaehler
$script:wid = 100
$nets += Network 'Stueckzaehler' 'Zaehlt Teile an der Lichtschranke bis zur Sollstueckzahl' @(
  (G 21 'B_Lichtschranke'), (G 22 'S_Quittieren'), (GConst 23 'C_MaxStueck'), (G 24 'MW_Stueckzahl'),
  ('<Call UId="30"><CallInfo Name="FB_Stueckzaehler" BlockType="FB"><Instance Scope="GlobalVariable" UId="31"><Component Name="DB_Zaehler_Band1" /></Instance>' +
   '<Parameter Name="Impuls" Section="Input" Type="Bool" /><Parameter Name="Reset" Section="Input" Type="Bool" /><Parameter Name="Sollwert" Section="Input" Type="Int" />' +
   '<Parameter Name="Istwert" Section="Output" Type="Int" /><Parameter Name="Erreicht" Section="Output" Type="Bool" /></CallInfo></Call>')
) @(
  (W (IC 21) (NC 30 'Impuls')), (W (IC 22) (NC 30 'Reset')), (W (IC 23) (NC 30 'Sollwert')), (W (NC 30 'Istwert') (IC 24))
)

# NW4: Drehzahl skalieren (FC mit Rueckgabewert)
$script:wid = 100
$nets += Network 'Drehzahl skalieren' 'Analogwert 0..27648 auf 0..1500 1/min' @(
  (G 21 'AI_Drehzahl'), (LC 22 'Real' '0.0'), (LC 23 'Real' '1500.0'), (G 24 'MD_Drehzahl'),
  ('<Call UId="30"><CallInfo Name="FC_Skalierung" BlockType="FC">' +
   '<Parameter Name="Rohwert" Section="Input" Type="Int" /><Parameter Name="Min" Section="Input" Type="Real" /><Parameter Name="Max" Section="Input" Type="Real" />' +
   '<Parameter Name="Ret_Val" Section="Return" Type="Real" /></CallInfo></Call>')
) @(
  (W (IC 21) (NC 30 'Rohwert')), (W (IC 22) (NC 30 'Min')), (W (IC 23) (NC 30 'Max')), (W (NC 30 'Ret_Val') (IC 24))
)

# NW5: Betriebsanzeige = Motor UND NICHT Stoerung
$script:wid = 100
$nets += Network 'Betriebsanzeige' 'Lampe Betrieb, solange der Motor ohne Stoerung laeuft' @(
  (G 21 'Q_Motor'), (G 22 'Q_LampeStoerung'), (G 23 'Q_LampeBetrieb'),
  '<Part Name="A" UId="24"><TemplateValue Name="Card" Type="Cardinality">2</TemplateValue><Negated Name="in2" /></Part>',
  '<Part Name="Coil" UId="25" />'
) @(
  (W (IC 21) (NC 24 'in1')), (W (IC 22) (NC 24 'in2')), (W (NC 24 'out') (NC 25 'in')), (W (IC 23) (NC 25 'operand'))
)

# NW6: Hupe bei Sollstueckzahl (Vergleicher + SR)
$script:wid = 100
$nets += Network 'Hupe bei Sollstueckzahl' 'Setzt die Hupe bei Erreichen der Sollstueckzahl, Quittieren setzt zurueck' @(
  (G 21 'MW_Stueckzahl'), (GConst 22 'C_MaxStueck'), (G 23 'S_Quittieren'), (G 24 'Q_Hupe'),
  '<Part Name="Ge" UId="25"><TemplateValue Name="SrcType" Type="Type">Int</TemplateValue></Part>',
  '<Part Name="Sr" UId="26" />'
) @(
  (W (IC 21) (NC 25 'in1')), (W (IC 22) (NC 25 'in2')), (W (NC 25 'out') (NC 26 's')), (W (IC 23) (NC 26 'r1')), (W (IC 24) (NC 26 'operand'))
)

# NW7: MOVE-Box (en bewusst NICHT verdrahtet)
$script:wid = 100
$nets += Network 'Sollwert uebernehmen' 'MOVE der Sollstueckzahl in ein Merkerwort' @(
  (GConst 21 'C_MaxStueck'), (G 22 'MW_Sollwert'),
  '<Part Name="Move" UId="23" DisabledENO="true"><TemplateValue Name="Card" Type="Cardinality">1</TemplateValue></Part>'
) @(
  (W (IC 21) (NC 23 'in')), (W (NC 23 'out1') (IC 22))
)

$blockComment
$blockComment = MLText 'Comment' 'Hauptprogramm Foerderband (FUP, per Openness-XML erzeugt)'
$blockTitle = MLText 'Title' 'Main Program Sweep (Cycle)'
$body = $nets -join "`r`n      "

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<Document>
  <Engineering version="V20" />
  <SW.Blocks.OB ID="0">
    <AttributeList>
      <Interface><Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
  <Section Name="Input">
    <Member Name="Initial_Call" Datatype="Bool" Informative="true" />
    <Member Name="Remanence" Datatype="Bool" Informative="true" />
  </Section>
  <Section Name="Temp" />
  <Section Name="Constant" />
</Sections></Interface>
      <MemoryLayout>Optimized</MemoryLayout>
      <Name>Main</Name>
      <Namespace />
      <Number>1</Number>
      <ProgrammingLanguage>FBD</ProgrammingLanguage>
      <SecondaryType>ProgramCycle</SecondaryType>
      <SetENOAutomatically>false</SetENOAutomatically>
    </AttributeList>
    <ObjectList>
      $blockComment
      $body
      $blockTitle
    </ObjectList>
  </SW.Blocks.OB>
</Document>
"@
[IO.File]::WriteAllText($Out, $xml, (New-Object Text.UTF8Encoding($false)))
"written $Out ($($xml.Length) chars)"
