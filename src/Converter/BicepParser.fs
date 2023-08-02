module rec Converter.BicepParser

open Bicep.Core.Parsing
open Bicep.Core.Syntax

type ParameterDeclaration = {
    name: string
    decorators: BicepSyntax list
    parameterType: string option
    defaultValue: BicepSyntax option
}

  with
    member this.description = this.decorators |> List.tryPick (function
        | BicepSyntax.FunctionCall ("description", [BicepSyntax.String description]) ->
            Some description
        | otherwise ->
            None)

type VariableDeclaration = {
    name: string
    value: BicepSyntax
}

type ResourceDeclaration = {
    name: string
    token: string
    value: BicepSyntax
}

type OutputDeclaration = {
    name: string
    value: BicepSyntax
}

type ModuleDeclaration = {
    name: string
    path: string
    value: BicepSyntax
}

[<RequireQualifiedAccess>]
type BicepDeclaration =
    | Parameter of ParameterDeclaration
    | Variable of VariableDeclaration
    | Resource of ResourceDeclaration
    | Output of OutputDeclaration
    | Module of ModuleDeclaration

[<RequireQualifiedAccess>]
type ProgramKind =
    | EntryPoint
    | Module

type BicepProgram = {
    declarations : BicepDeclaration list
    programKind: ProgramKind
}

type BicepForSyntax = {
    variableSection : BicepSyntax
    expression : BicepSyntax
    body : BicepSyntax
}

[<RequireQualifiedAccess>]
type BicepSyntax =
    | FunctionCall of name:string * arguments:BicepSyntax list
    | InterpolatedString of expressions: BicepSyntax list * segmentValues: string list
    | String of string
    | Integer of value: int64
    | Boolean of value: bool
    | Array of items: BicepSyntax list
    | Object of properties: Map<BicepSyntax, BicepSyntax>
    | PropertyAccess of target: BicepSyntax * property: string
    | VariableAccess of name: string
    | Identifier of name:string
    | IfCondition of condition: BicepSyntax * body: BicepSyntax
    | For of BicepForSyntax
    | LocalVariable of name: string
    | LocalVariableBlock of names: string list
    | UnaryExpression of operator: string * operand: BicepSyntax
    | BinaryExpression of operator: string * left: BicepSyntax * right: BicepSyntax
    | TernaryExpression of condition: BicepSyntax * trueValue: BicepSyntax * falseValue: BicepSyntax
    | IndexExpression of target: BicepSyntax * index: BicepSyntax
    | Null
    | Empty

let rec readSyntax (input: SyntaxBase) : BicepSyntax =
    match input with
    | :? FunctionCallSyntax as functionCall ->
        let name = functionCall.Name.IdentifierName
        let arguments = functionCall.Arguments |> Seq.map readSyntax |> List.ofSeq
        BicepSyntax.FunctionCall (name, arguments)
        
    | :? StringSyntax as stringSyntax ->
        let expressions = stringSyntax.Expressions |> Seq.map readSyntax |> List.ofSeq
        let stringParts = stringSyntax.SegmentValues |> List.ofSeq
        
        if List.isEmpty expressions && not (List.isEmpty stringParts) then
            BicepSyntax.String (String.concat "" stringParts)
        else
            BicepSyntax.InterpolatedString (expressions, stringParts)

    | :? IntegerLiteralSyntax as integerSyntax ->
        BicepSyntax.Integer (int64 integerSyntax.Value)

    | :? BooleanLiteralSyntax as booleanSyntax ->
        BicepSyntax.Boolean booleanSyntax.Value
    
    | :? SeparatedSyntaxList as arraySyntax ->
        let items = arraySyntax.Elements |> Seq.map readSyntax |> List.ofSeq
        BicepSyntax.Array items
        
    | :? NullLiteralSyntax ->
        BicepSyntax.Null
    
    | :? ObjectSyntax as objectSyntax ->
         let properties = Map.ofList [
            for property in objectSyntax.Properties do
                let name = readSyntax property.Key
                let value = readSyntax property.Value
                name, value    
         ]
         
         BicepSyntax.Object properties

    | :? ParameterDefaultValueSyntax as defaultValueSyntax ->
        readSyntax defaultValueSyntax.DefaultValue
        
    | :? DecoratorSyntax as decoratorSyntax ->
        readSyntax decoratorSyntax.Expression
    
    | :? FunctionArgumentSyntax as argumentSyntax ->
        readSyntax argumentSyntax.Expression

    | :? ParenthesizedExpressionSyntax as parenthesizedExpression ->
        readSyntax parenthesizedExpression.Expression

    | :? PropertyAccessSyntax as propertyAccess ->
        let target = readSyntax propertyAccess.BaseExpression
        let property = propertyAccess.PropertyName.IdentifierName
        BicepSyntax.PropertyAccess (target, property)

    | :? VariableAccessSyntax as variableAccess ->
        BicepSyntax.VariableAccess variableAccess.Name.IdentifierName

    | :? IdentifierSyntax as identifier ->
        BicepSyntax.Identifier identifier.IdentifierName

    | :? IfConditionSyntax as ifCondition ->
        let condition = readSyntax ifCondition.ConditionExpression
        let body = readSyntax ifCondition.Body
        BicepSyntax.IfCondition (condition, body)

    | :? ForSyntax as forSyntax ->
        let variableSection = readSyntax forSyntax.VariableSection
        let expression = readSyntax forSyntax.Expression
        let body = readSyntax forSyntax.Body
        BicepSyntax.For {
            variableSection = variableSection
            expression = expression
            body = body
        }

    | :? UnaryOperationSyntax as unarySyntax ->
        let operator = unarySyntax.OperatorToken.Text
        let operand = readSyntax unarySyntax.Expression
        BicepSyntax.UnaryExpression (operator, operand)
        
    | :? ArrayAccessSyntax as arrayAccess ->
        let target = readSyntax arrayAccess.BaseExpression
        let index = readSyntax arrayAccess.IndexExpression
        BicepSyntax.IndexExpression (target, index)

    | :? BinaryOperationSyntax as binarySyntax ->
        let operator = binarySyntax.OperatorToken.Text
        let left = readSyntax binarySyntax.LeftExpression
        let right = readSyntax binarySyntax.RightExpression
        BicepSyntax.BinaryExpression (operator, left, right)

    | :? TernaryOperationSyntax as ternarySyntax ->
        let condition = readSyntax ternarySyntax.ConditionExpression
        let trueValue = readSyntax ternarySyntax.TrueExpression
        let falseValue = readSyntax ternarySyntax.FalseExpression
        BicepSyntax.TernaryExpression (condition, trueValue, falseValue)

    | :? ArrayItemSyntax as arrayItem ->
        readSyntax arrayItem.Value

    | :? ArraySyntax as arraySyntax ->
        let items = arraySyntax.Items |> Seq.map readSyntax |> List.ofSeq
        BicepSyntax.Array items

    | :? LocalVariableSyntax as localVariable ->
        BicepSyntax.LocalVariable localVariable.Name.IdentifierName

    | :? VariableBlockSyntax as variableBlock ->
        variableBlock.Arguments
        |> Seq.map (fun localVar -> localVar.Name.IdentifierName)
        |> List.ofSeq
        |> BicepSyntax.LocalVariableBlock

    | otherwise ->
        BicepSyntax.Empty

let withoutVersion (typeToken: string) =
    match typeToken.Split "@" with
    | [| token; version |] -> token
    | _ -> typeToken
    
let addParentSymbol (parentSymbol: string) (resourceValue: BicepSyntax) =
    match resourceValue with
    | BicepSyntax.Object properties ->
        if not (Map.containsKey (BicepSyntax.Identifier "parent") properties) then
            properties
            |> Map.add (BicepSyntax.Identifier "parent") (BicepSyntax.VariableAccess parentSymbol)
            |> BicepSyntax.Object
            
        else
            BicepSyntax.Object properties
    | _ ->
        resourceValue

let parseProgram (program: ProgramSyntax) : Result<BicepProgram, string> =
  try 
      let declarations = ResizeArray<BicepDeclaration>()

      let rec parseChildResources (value: SyntaxBase) (parentResourceToken: string) (parentResourceSymbol: string) =
          match value with
          | :? ObjectSyntax as objectValue ->
              for resource in objectValue.Resources do
                  match readSyntax (resource.TypeString :> SyntaxBase) with
                  | BicepSyntax.String typeToken ->
                      let fullResourceToken =
                          let token = withoutVersion typeToken
                          if  not (token.Contains ".")
                          then parentResourceToken.Replace("@", $"/{token}@")
                          else typeToken

                      declarations.Add(BicepDeclaration.Resource {
                          name = resource.Name.IdentifierName
                          token = fullResourceToken
                          value = readSyntax resource.Value |> addParentSymbol parentResourceSymbol
                      })

                      parseChildResources resource.Value fullResourceToken resource.Name.IdentifierName
                  | _ ->
                      ()
          | _ ->
              ()
      
      for decl in program.Declarations do
          match decl with
          | :? ParameterDeclarationSyntax as parameter ->
              let name = parameter.Name.IdentifierName
              let decorators =
                  parameter.Decorators
                  |> Seq.map readSyntax
                  |> List.ofSeq
                  
              let parameterType =
                  match parameter.Type with
                  | :? VariableAccessSyntax as paramType ->
                      Some paramType.Name.IdentifierName
                  | _ ->
                      None
                      
              let defaultValue =
                  if isNull parameter.Modifier
                  then None
                  else Some (readSyntax parameter.Modifier)

              declarations.Add (BicepDeclaration.Parameter {
                  name = name
                  decorators = decorators
                  defaultValue = defaultValue
                  parameterType = parameterType
              })

          | :? VariableDeclarationSyntax as variable ->
              declarations.Add(BicepDeclaration.Variable {
                  name = variable.Name.IdentifierName
                  value = readSyntax variable.Value
              })

          | :? ResourceDeclarationSyntax as resource ->
              match readSyntax (resource.TypeString :> SyntaxBase) with
              | BicepSyntax.String typeString ->
                    if not (resource.IsExistingResource()) then
                        declarations.Add(BicepDeclaration.Resource {
                            name = resource.Name.IdentifierName
                            token = typeString
                            value = readSyntax resource.Value
                        })
                        
                        parseChildResources resource.Value typeString resource.Name.IdentifierName
                    else
                        declarations.Add(BicepDeclaration.Variable {
                            name = resource.Name.IdentifierName
                            value = BicepSyntax.FunctionCall(Tokens.GetExistingResource, [
                                BicepSyntax.String typeString
                                readSyntax resource.Value
                            ])
                        })
              | _ ->
                  ()

          | :? OutputDeclarationSyntax as output ->
                declarations.Add(BicepDeclaration.Output {
                    name = output.Name.IdentifierName
                    value = readSyntax output.Value
                })
 
          | :? ModuleDeclarationSyntax as moduleDecl ->
              match readSyntax moduleDecl.Path with
              | BicepSyntax.String path ->
                    declarations.Add(BicepDeclaration.Module {
                        name = moduleDecl.Name.IdentifierName
                        path = path
                        value = readSyntax moduleDecl.Value
                    })

              | _ ->
                  ()
          | _ ->
              ()
      Ok {
          declarations = List.ofSeq declarations
          programKind = ProgramKind.EntryPoint
      }
  with
  | ex -> Error ex.Message
  
  
let parse (text: string) = 
   let parser = Parser(text)
   let program = parser.Program()
   parseProgram program

let parseOrFail (source: string) =
    match parse source with
    | Ok program -> program
    | Error message -> failwith message

let parseExpression (source: string) =
    let parser = Parser(source)
    let expression = parser.Expression(ExpressionFlags.AllowComplexLiterals)
    readSyntax expression