using System.Xml.Serialization;

namespace Pam.Ingest.Vendors.TwentyOneG.Soap;

// Response shape every 21G PostTransaction endpoint returns. Matches the
// `TransactionResult` complexType in the live WSDLs:
//
//   <s:complexType name="TransactionResult">
//     <s:sequence>
//       <s:element minOccurs="1" maxOccurs="1" name="DocumentNumber"   type="s:int"/>
//       <s:element minOccurs="0" maxOccurs="1" name="RespMessage"      type="s:string"/>
//       <s:element minOccurs="1" maxOccurs="1" name="AvailableBalance" type="s:int"/>
//     </s:sequence>
//   </s:complexType>
//
// DocumentNumber: GBS-side transaction id. During Phase A (intercept-
// and-forward) we surface GBS's value verbatim. Until the forwarder is
// wired, we return 0.
//
// AvailableBalance: balance after the transaction, in cents (per
// GBS convention — balance fields on the wire are in cents/integer units).
[XmlType(Namespace = TwentyOneGSoapDefaults.Namespace)]
public sealed class TransactionResult
{
    public int DocumentNumber { get; set; }

    public string? RespMessage { get; set; }

    public int AvailableBalance { get; set; }
}
