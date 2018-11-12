namespace Helpers

open System
open System.Collections.Generic
open System.Linq
open System.Text
open System.Threading.Tasks
open System.Globalization
open System.Windows.Data

type MidPointConverter() =
    interface IMultiValueConverter with
        member this.Convert(values, targetType, parameter, culture) =
        // might generalize by subtracting midpoints of all values >= 1
         try
           ((float (values.[0].ToString())) - (float (values.[1].ToString()) / 2.0)) :> obj
         with _ -> 0.0 :> obj

        member this.ConvertBack(value,targetType,parameter,culture) =
            failwith "Not implemented"

type HalfingConverter() =
  //  member val ReferenceValue = Unchecked.defaultof<float> with get,set
    interface IValueConverter with
        member this.Convert(values, targetType, parameter, culture) =
        // might generalize by subtracting midpoints of all values >= 1
         try
            -(float (values.ToString())) / 2.0 :> obj
         with _ -> 0.0 :> obj

        member this.ConvertBack(value,targetType,parameter,culture) =
            failwith "Not implemented"

