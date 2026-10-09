namespace Devlooped.DataAnnotations
{
    /// <summary>
    /// Marks a factory whose closed type arguments and return type are validation targets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[Validated] static IBox&lt;T&gt; Create&lt;T&gt;()</c> registers <c>IBox&lt;int&gt;</c> at a call
    /// <c>Create&lt;int&gt;()</c>. The return type is included, so a method that returns
    /// <c>Repository&lt;T&gt;</c> registers that closed repository rather than only <c>T</c>.
    /// </para>
    /// <para>
    /// A generic wrapper must be marked too. <c>Make&lt;T&gt;() => Create&lt;IBox&lt;T&gt;&gt;()</c> does not
    /// close <c>IBox&lt;int&gt;</c> at the <c>Create</c> call written inside it. <c>[Validated]</c> on
    /// <c>Make</c> tells the generator to substitute <c>Make&lt;int&gt;()</c> through factories it calls,
    /// including other <see cref="ValidatedAttribute"/> methods, up to eight levels.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ValidatedAttribute : Attribute
    {
    }

    /// <summary>
    /// Registers <typeparamref name="T"/> for validation when no factory call in the project closes over that type.
    /// </summary>
    /// <remarks>
    /// Use this for a closed type that is only built from a run-time <see cref="Type"/>:
    /// <c>[assembly: Validate&lt;IBox&lt;int&gt;&gt;]</c>. Repeat the attribute for each closed type.
    /// </remarks>
    /// <typeparam name="T">The closed type whose annotated members are registered.</typeparam>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class ValidateAttribute<T> : Attribute
    {
    }
}
