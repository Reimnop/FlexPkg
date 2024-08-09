﻿// <auto-generated />
using System;
using FlexPkg.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FlexPkg.MySqlMigrations.Migrations
{
    [DbContext(typeof(FlexPkgContext))]
    [Migration("20240809101415_Initial")]
    partial class Initial
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.7")
                .HasAnnotation("Relational:MaxIdentifierLength", 64);

            MySqlModelBuilderExtensions.AutoIncrementColumns(modelBuilder);

            modelBuilder.Entity("FlexPkg.Database.SteamAccount", b =>
                {
                    b.Property<string>("Username")
                        .HasColumnType("varchar(255)");

                    b.Property<string>("Token")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.HasKey("Username");

                    b.ToTable("SteamAccounts");
                });

            modelBuilder.Entity("FlexPkg.Database.SteamAppManifest", b =>
                {
                    b.Property<ulong>("Id")
                        .HasColumnType("bigint unsigned");

                    b.Property<string>("BranchName")
                        .HasColumnType("varchar(255)");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<bool>("Handled")
                        .HasColumnType("tinyint(1)");

                    b.Property<string>("PatchNotes")
                        .HasColumnType("longtext");

                    b.Property<string>("Version")
                        .HasColumnType("longtext");

                    b.HasKey("Id", "BranchName");

                    b.ToTable("SteamAppManifests");
                });
#pragma warning restore 612, 618
        }
    }
}
