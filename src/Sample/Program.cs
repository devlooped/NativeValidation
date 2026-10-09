using System.ComponentModel.DataAnnotations;

namespace Devlooped.Sample;

static class Program
{
    static int Main()
    {
        var valid = SampleOrder();
        if (!ValidateGraph(valid, out var validErrors))
        {
            Console.Error.WriteLine("Expected the sample order to pass.");
            Write(validErrors);
            return 1;
        }

        valid.Buyer!.ConfirmPassword = "different";
        if (ValidateGraph(valid, out var passwordErrors) ||
            !passwordErrors.Exists(error => error.MemberNames.Contains(nameof(Customer.ConfirmPassword))))
        {
            Console.Error.WriteLine("Expected confirm password to fail.");
            return 1;
        }

        var shipped = SampleOrder();
        shipped.ShipToBillingAddress = false;
        shipped.ShippingAddress = new Address();
        if (Passes(shipped) || !FailsProperty(shipped, nameof(Order.ShippingAddress)))
        {
            Console.Error.WriteLine("Expected a shipping address when it differs from billing.");
            return 1;
        }

        Console.WriteLine("Sample order validated.");
        return 0;
    }

    static bool Passes(object instance)
    {
        var errors = new List<ValidationResult>();
        return NativeValidator.TryValidateObject(instance, Context(instance), errors, validateAllProperties: true);
    }

    static bool FailsProperty(object instance, string member)
    {
        var errors = new List<ValidationResult>();
        NativeValidator.TryValidateObject(instance, Context(instance), errors, validateAllProperties: true);
        return errors.Exists(error => error.MemberNames.Contains(member));
    }

    static bool ValidateGraph(Order order, out List<ValidationResult> errors)
    {
        errors = [];
        var objects = new object?[]
        {
            order,
            order.Buyer,
            order.Buyer?.Home,
            order.Buyer?.Preferences,
            order.BillingAddress,
            order.ShipToBillingAddress ? null : order.ShippingAddress,
            order.Payment,
        };

        var valid = true;
        foreach (var instance in objects)
        {
            if (instance == null)
                continue;
            if (!NativeValidator.TryValidateObject(instance, Context(instance), errors, validateAllProperties: true))
                valid = false;
        }

        if (order.Lines != null)
        {
            foreach (var line in order.Lines)
            {
                if (!NativeValidator.TryValidateObject(line, Context(line), errors, validateAllProperties: true))
                    valid = false;
                if (line.Item != null && !NativeValidator.TryValidateObject(line.Item, Context(line.Item), errors, validateAllProperties: true))
                    valid = false;
            }
        }

        return valid;
    }

    static ValidationContext Context(object instance) =>
        new(instance, instance.GetType().Name, null, null);

    static void Write(List<ValidationResult> errors)
    {
        foreach (var error in errors)
            Console.Error.WriteLine(error.ErrorMessage);
    }

    static Order SampleOrder() => new()
    {
        Number = "A-10042",
        Status = OrderStatus.Placed,
        PlacedOn = new DateOnly(2026, 3, 1),
        Coupon = "SPRING",
        ShipToBillingAddress = true,
        Shipping = 12,
        Notes = "Leave at the side door.",
        Buyer = new Customer
        {
            Name = "Ada Lovelace",
            Email = "ada@example.com",
            ConfirmEmail = "ada@example.com",
            Password = "Analytical1",
            ConfirmPassword = "Analytical1",
            Phone = "425-555-0100 x12",
            Website = "https://example.com",
            Avatar = "YQ==",
            Photo = "ada.png",
            Channels = NotificationChannels.Email | NotificationChannels.Push,
            Home = Home(),
            Preferences = new CustomerPreferences
            {
                Language = "en",
                Currency = "USD",
                QuietHoursStart = new TimeOnly(9, 0),
            },
        },
        BillingAddress = Home(),
        Payment = new PaymentCard
        {
            Cardholder = "Ada Lovelace",
            Number = "4111111111111111",
            Expires = new DateOnly(2028, 6, 1),
            Cvv = "123",
        },
        Lines =
        [
            new OrderLine
            {
                Quantity = 2,
                UnitPrice = 18.5,
                Item = new Product
                {
                    Sku = "ENG-01",
                    Name = "Analytical engine print",
                    Stock = 12,
                    ImageUrl = "https://example.com/engine.png",
                },
            },
        ],
    };

    static Address Home() => new()
    {
        Line1 = "12 St James Square",
        City = "London",
        Region = "Greater London",
        PostalCode = "SW1Y-4LB",
        Country = "US",
    };
}
